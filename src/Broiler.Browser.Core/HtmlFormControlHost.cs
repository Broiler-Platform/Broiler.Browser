using System.Drawing;
using Broiler.Dom;
using Broiler.Dom.Html;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.HTML.Image;
using Broiler.HtmlBridge;
using Broiler.UI;
using Broiler.UI.Button;
using Broiler.UI.Button.Standard;
using Broiler.UI.CheckBox;
using Broiler.UI.CheckBox.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.RadioButton;
using Broiler.UI.RadioButton.Standard;

namespace Broiler.Browser;

/// <summary>
/// Hosts real Broiler.UI controls over the page's checkboxes, radios and
/// <c>&lt;select&gt;</c>s — a <see cref="StandardCheckBox"/>,
/// <see cref="StandardRadioButton"/> or <see cref="StandardComboBox"/> apiece. The
/// renderer draws all three as empty bordered boxes: no check, no dot, no selected
/// option text, no popup, and no way to change any of them.
/// </summary>
/// <remarks>
/// <para>
/// Unlike the text editor — which hosts one control on demand, at the point the user
/// clicked — these have to show their state whether or not they are being interacted
/// with, so every one on the page is hosted up front.
/// </para>
/// <para>
/// That needs each control's geometry, and the renderer's only public geometry query
/// for an arbitrary element is <c>GetElementRectangle(id)</c>. Controls are therefore
/// given synthetic ids by <see cref="HtmlPostProcessor.StampFormControlIds"/> before
/// the page reaches the renderer, identified from the parsed document, and located by
/// id after each layout.
/// </para>
/// <para>
/// Changes record into <see cref="HtmlFormState"/> rather than the document: the
/// renderer has no API to write a <c>checked</c> attribute or a selected option back,
/// and form submission layers this state over the markup anyway.
/// </para>
/// <para>
/// A page with a scripting session holds its controls' state itself and reflects it into the markup the
/// window draws (<see cref="HtmlFormState.PageHoldsState"/>), and its scripts change it after the controls are
/// hosted: <see cref="Sync"/> brings them into line with each new rendering of the page.
/// </para>
/// </remarks>
internal sealed class HtmlFormControlHost
{
    /// <summary>
    /// Ceiling on hosted controls per page. Each one costs an id lookup — a box-tree
    /// walk — on every layout, so a pathological page is capped rather than allowed
    /// to make scrolling quadratic.
    /// </summary>
    private const int MaxHostedControls = 64;

    /// <summary>UA default control font (see <c>CssDefaults</c>), so hosted controls match the page.</summary>
    private const string UaControlFontFamily = "Arial";
    private const double UaControlFontSize = 13.3333;

    /// <summary>What a file control reads before anything is chosen.</summary>
    private const string NoFileChosen = "Choose File";

    /// <summary>What a <c>multiple</c> file control reads before anything is chosen.</summary>
    private const string NoFilesChosen = "Choose Files";

    private readonly UiElement _owner;
    private readonly HtmlFormState _formState;
    private readonly List<HostedToggle> _hosted = [];
    private readonly Dictionary<string, UiRadioGroupScope> _radioGroups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ToggleIdentity>> _radioMembers = new(StringComparer.Ordinal);
    private readonly List<FilePicker> _filePickers = [];

    /// <summary>
    /// Set while <see cref="Sync"/> sets the controls to the page's state, so a control changed to show what the
    /// page did does not report it back as the user's choice.
    /// </summary>
    private bool _syncing;

    public HtmlFormControlHost(UiElement owner, HtmlFormState formState)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _formState = formState ?? throw new ArgumentNullException(nameof(formState));
    }

    /// <summary>Raised when the user changed a control, so the host can repaint.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raised when the user activated a file control. The host does not own a window,
    /// so choosing the file is the browser's job; it records the result through
    /// <see cref="HtmlFormState.SetSelectedFile"/> and calls
    /// <see cref="RefreshFileLabels"/>.
    /// </summary>
    public event EventHandler<HtmlFilePickEventArgs>? FilePickRequested;

    /// <summary>
    /// Raised when the user chose an option of a hosted select: which select of the page, and which option of
    /// it, both counted in tree order, so the host can tell the page's select.
    /// </summary>
    public event EventHandler<HtmlOptionChosenEventArgs>? OptionChosen;

    /// <summary>
    /// Raised when the user changed the options chosen in a <c>&lt;select multiple&gt;</c>'s list, with every
    /// option chosen now; the page's select takes them all.
    /// </summary>
    public event EventHandler<HtmlOptionsChosenEventArgs>? OptionsChosen;

    /// <summary>The controls hosted for the current page.</summary>
    public IReadOnlyList<UiElement> Controls => _hosted.ConvertAll(h => h.Control);

    /// <summary>Number of controls hosted, for tests and diagnostics.</summary>
    public int Count => _hosted.Count;

    /// <summary>
    /// Rebuilds the hosted controls from <paramref name="pageHtml"/>. Call once per
    /// page, not per layout — the parse is the expensive half.
    /// </summary>
    public void Rebuild(string pageHtml)
    {
        Clear();

        DomElement? root = TryParse(pageHtml);
        if (root is null)
            return;

        foreach (ControlSite site in FindControls(root))
            Add(site);
    }

    /// <summary>
    /// Brings the hosted controls into line with <paramref name="pageHtml"/>, a later rendering of the page:
    /// the option a select shows, the options a multiple select's list has selected, whether a checkbox or a
    /// radio button is checked. When the page still has the same controls, with the same options, they are
    /// kept -- a drop-down the user has open stays open -- and set to the page's state; when it has others,
    /// they are hosted again.
    /// </summary>
    /// <remarks>
    /// The controls were hosted once per page, so what the page's scripts did after that was never shown: a
    /// page that answered the user's choice of an option by choosing another went on showing the user's, and
    /// a box a script ticked stayed empty. Setting a control here does not tell the page the user chose it.
    /// A file control's label is the window's record of what the user picked; the markup carries no files.
    /// </remarks>
    public void Sync(string pageHtml)
    {
        // A page with nothing hosted and nothing to host costs no parse.
        if (string.IsNullOrEmpty(pageHtml) ||
            (_hosted.Count == 0 &&
             pageHtml.IndexOf("<select", StringComparison.OrdinalIgnoreCase) < 0 &&
             pageHtml.IndexOf("<input", StringComparison.OrdinalIgnoreCase) < 0))
        {
            return;
        }

        DomElement? root = TryParse(pageHtml);
        if (root is null)
            return;

        List<ControlSite> sites = [.. FindControls(root)];
        bool same = sites.Count == _hosted.Count;
        for (int index = 0; same && index < sites.Count; index++)
            same = string.Equals(Shape(sites[index]), _hosted[index].Shape, StringComparison.Ordinal);

        if (!same)
        {
            Clear();
            foreach (ControlSite site in sites)
                Add(site);
            return;
        }

        _syncing = true;
        try
        {
            for (int index = 0; index < sites.Count; index++)
                _hosted[index].Apply(sites[index].Element);
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>
    /// The page's controls to host, in tree order, up to <see cref="MaxHostedControls"/>: its selects, and its
    /// checkboxes, radio buttons and file inputs, each with an id.
    /// </summary>
    private static IEnumerable<ControlSite> FindControls(DomElement root)
    {
        // Each select and file input is the page's by its place among the page's selects or file inputs,
        // counted in tree order as the page's bridge counts them.
        int count = 0;
        int selectIndex = -1;
        int fileIndex = -1;
        foreach (DomElement element in Descendants(root))
        {
            if (count >= MaxHostedControls)
                yield break;

            bool isSelect = string.Equals(element.TagName, "select", StringComparison.OrdinalIgnoreCase);
            if (isSelect)
                selectIndex++;
            bool isInput = string.Equals(element.TagName, "input", StringComparison.OrdinalIgnoreCase);
            if (!isSelect && !isInput)
                continue;

            string type = (element.GetAttribute("type") ?? string.Empty).Trim();
            bool isRadio = isInput && string.Equals(type, "radio", StringComparison.OrdinalIgnoreCase);
            bool isCheckBox = isInput && string.Equals(type, "checkbox", StringComparison.OrdinalIgnoreCase);
            bool isFile = isInput && string.Equals(type, "file", StringComparison.OrdinalIgnoreCase);
            if (isFile)
                fileIndex++;
            if (!isSelect && !isRadio && !isCheckBox && !isFile)
                continue;

            string id = element.GetAttribute("id") ?? string.Empty;
            if (id.Length == 0)
                continue;

            count++;
            yield return new ControlSite(element, id, isSelect, isRadio, isFile, isFile ? fileIndex : selectIndex);
        }
    }

    /// <summary>
    /// What makes a hosted control the same one in a later rendering of the page: its kind, id, name, value,
    /// <c>multiple</c> and place among the page's selects or file inputs, and a select's options.
    /// </summary>
    private static string Shape(ControlSite site)
    {
        DomElement element = site.Element;
        System.Text.StringBuilder shape = new();
        shape.Append(site.IsFile ? 'f' : site.IsSelect ? 's' : site.IsRadio ? 'r' : 'c')
            .Append('\u0001').Append(site.Id)
            .Append('\u0001').Append(element.GetAttribute("name") ?? string.Empty)
            .Append('\u0001').Append(element.GetAttribute("value") ?? string.Empty)
            .Append('\u0001').Append(element.HasAttribute("multiple") ? '1' : '0')
            .Append('\u0001').Append(site.PageIndex);
        if (site.IsSelect)
        {
            foreach (DomElement option in Options(element))
            {
                shape.Append('\u0002').Append(option.GetAttribute("value") ?? "\u0003")
                    .Append('\u0001').Append((option.TextContent ?? string.Empty).Trim());
            }
        }

        return shape.ToString();
    }

    /// <summary>Removes every hosted control. Called when the page is replaced.</summary>
    public void Clear()
    {
        foreach (HostedToggle hosted in _hosted)
        {
            _owner.RemoveChild(hosted.Control);
            hosted.Control.Dispose();
        }

        _hosted.Clear();
        _radioGroups.Clear();
        _radioMembers.Clear();
        _filePickers.Clear();
    }

    /// <summary>
    /// Places every hosted control over its element for the current viewport
    /// transform, hiding those that are scrolled out of view or no longer laid out.
    /// </summary>
    public void UpdateViewport(HtmlContainer? container, BRect viewportBounds, double zoom, double scrollY)
    {
        if (_hosted.Count == 0)
            return;

        foreach (HostedToggle hosted in _hosted)
        {
            RectangleF? rect = container is null || zoom <= 0 || viewportBounds.IsEmpty
                ? null
                : TryGetRectangle(container, hosted.ElementId);

            if (rect is not { Width: > 0, Height: > 0 } box)
            {
                hosted.Control.Visibility = UiVisibility.Collapsed;
                continue;
            }

            BRect target = new(
                viewportBounds.Left + (box.X * zoom),
                viewportBounds.Top + (box.Y * zoom) - scrollY,
                box.Width * zoom,
                box.Height * zoom);

            if (target.Intersect(viewportBounds).IsEmpty)
            {
                hosted.Control.Visibility = UiVisibility.Collapsed;
                continue;
            }

            hosted.Control.Visibility = UiVisibility.Visible;
            ApplyMarkSize(hosted.Control, target);
            hosted.Control.Measure(target.Size);
            hosted.Control.Arrange(target);
        }
    }

    private void Add(ControlSite site)
    {
        DomElement element = site.Element;
        string id = site.Id;
        string name = element.GetAttribute("name") ?? string.Empty;
        string value = element.GetAttribute("value") ?? string.Empty;

        UiElement control;
        Action<DomElement> apply;
        if (site.IsFile)
        {
            control = CreateFilePicker(id, name, element.HasAttribute("multiple"), site.PageIndex);
            apply = static _ => { };
        }
        else if (site.IsSelect && element.HasAttribute("multiple"))
        {
            StandardListView list = CreateListView(element, id, name, site.PageIndex);
            control = list;
            apply = markup =>
            {
                List<string> shown = ShownOptions(markup, id, name);
                if (!list.SelectedItemIds.ToHashSet(StringComparer.Ordinal).SetEquals(shown))
                    list.SetSelectedItems(shown);
            };
        }
        else if (site.IsSelect)
        {
            StandardComboBox combo = CreateComboBox(element, id, name, site.PageIndex);
            control = combo;
            apply = markup =>
            {
                int shown = ShownOption(markup, id, name);
                if (shown >= 0 && combo.SelectedIndex != shown)
                    combo.SelectIndex(shown);
            };
        }
        else if (site.IsRadio)
        {
            StandardRadioButton radio = CreateRadio(id, name, value, IsChecked(element, id, name, value));
            control = radio;
            apply = markup =>
            {
                bool isChecked = IsChecked(markup, id, name, value);
                if (radio.IsChecked != isChecked)
                    radio.IsChecked = isChecked;
            };
        }
        else
        {
            StandardCheckBox box = CreateCheckBox(id, name, value, IsChecked(element, id, name, value));
            control = box;
            apply = markup =>
            {
                bool isChecked = IsChecked(markup, id, name, value);
                if (box.IsChecked != isChecked)
                    box.IsChecked = isChecked;
            };
        }

        // A disabled control takes no input: the hosted one is disabled with it, and follows the page's
        // scripts as they disable it or enable it again. It was always enabled, so the disabled radios
        // of reCAPTCHA's demo form could be ticked.
        SetEnabled(control, !IsActuallyDisabled(element));
        control.Visibility = UiVisibility.Collapsed;
        _owner.AddChild(control);
        _hosted.Add(new HostedToggle(id, control, Shape(site), markup =>
        {
            apply(markup);
            SetEnabled(control, !IsActuallyDisabled(markup));
        }));
    }

    /// <summary>
    /// Enables or disables a hosted control. A multiple select's list has no disabled state to show,
    /// and stays as it is. So does a checkbox, for now: Broiler.UI 0.1.0-preview.18 draws a disabled
    /// checked box's tick white on white, and a box the page has checked would look unchecked.
    /// </summary>
    private static void SetEnabled(UiElement control, bool enabled)
    {
        switch (control)
        {
            case UiRadioButton radio:
                radio.IsEnabled = enabled;
                break;
            case UiComboBox combo:
                combo.IsEnabled = enabled;
                break;
            case UiButton button:
                button.IsEnabled = enabled;
                break;
        }
    }

    /// <summary>
    /// Whether a form control is disabled (HTML §4.10.18.5): it has a <c>disabled</c> attribute, or
    /// is in a disabled <c>&lt;fieldset&gt;</c> and not in that fieldset's first <c>&lt;legend&gt;</c>.
    /// </summary>
    private static bool IsActuallyDisabled(DomElement element)
    {
        if (element.HasAttribute("disabled"))
            return true;

        DomNode child = element;
        for (DomNode? node = element.ParentNode; node is DomElement ancestor; child = ancestor, node = ancestor.ParentNode)
        {
            if (string.Equals(ancestor.TagName, "fieldset", StringComparison.OrdinalIgnoreCase)
                && ancestor.HasAttribute("disabled")
                && !ReferenceEquals(child, FirstLegend(ancestor)))
            {
                return true;
            }
        }

        return false;

        static DomElement? FirstLegend(DomElement fieldset)
        {
            foreach (DomNode node in fieldset.ChildNodes)
            {
                if (node is DomElement first && string.Equals(first.TagName, "legend", StringComparison.OrdinalIgnoreCase))
                    return first;
            }

            return null;
        }
    }

    /// <summary>Whether a checkbox or radio button shows checked: as the user left it on a page that keeps no state of its own, else as the markup has it.</summary>
    private bool IsChecked(DomElement element, string id, string name, string value) =>
        _formState.GetChecked(id, name, value) ?? element.HasAttribute("checked");

    private StandardCheckBox CreateCheckBox(string id, string name, string value, bool isChecked)
    {
        StandardCheckBox box = new()
        {
            IsChecked = isChecked,
            Text = string.Empty,
            PaddingX = 0,
            PaddingY = 0,
            Background = BColor.White,
        };

        box.CheckStateChanged += (_, _) =>
        {
            if (_syncing)
                return;

            _formState.SetChecked(id, name, value, box.CheckState == UiCheckState.Checked);
            Changed?.Invoke(this, EventArgs.Empty);
        };

        return box;
    }

    private StandardRadioButton CreateRadio(string id, string name, string value, bool isChecked)
    {
        if (!_radioGroups.TryGetValue(name, out UiRadioGroupScope? scope))
        {
            scope = new UiRadioGroupScope(name);
            _radioGroups[name] = scope;
        }

        StandardRadioButton radio = new()
        {
            GroupScope = scope,
            IsChecked = isChecked,
            Text = string.Empty,
            PaddingX = 0,
            PaddingY = 0,
            Background = BColor.White,
        };

        if (!_radioMembers.TryGetValue(name, out List<ToggleIdentity>? members))
        {
            members = [];
            _radioMembers[name] = members;
        }

        ToggleIdentity identity = new(id, name, value);
        members.Add(identity);

        radio.CheckedChanged += (_, _) =>
        {
            if (_syncing)
                return;

            // A radio group is single-choice, and the markup's `checked` on a sibling
            // would otherwise still be submitted. Selecting one records the whole
            // group, so the untouched siblings are explicitly unchecked rather than
            // falling back to the markup.
            if (radio.IsChecked)
            {
                foreach (ToggleIdentity member in members)
                    _formState.SetChecked(member.Id, member.Name, member.Value, member == identity);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        };

        return radio;
    }

    /// <summary>
    /// Builds a combo box from a <c>&lt;select&gt;</c>'s options, seeded with the
    /// selection the page declares (or the first option, which is what a single-select
    /// with nothing marked shows and submits).
    /// </summary>
    private StandardComboBox CreateComboBox(DomElement select, string id, string name, int selectIndex)
    {
        List<UiComboBoxItem> items = [];
        List<string> values = [];

        foreach (DomElement option in Options(select))
        {
            string text = (option.TextContent ?? string.Empty).Trim();
            // An option with no value attribute submits its text.
            string value = option.GetAttribute("value") ?? text;

            values.Add(value);
            items.Add(new UiComboBoxItem(value, text));
        }

        int selected = ShownOption(select, id, name);

        StandardComboBox combo = new()
        {
            Font = new BFontStyle(UaControlFontFamily, UaControlFontSize),
            CornerRadius = 0,
        };
        combo.SetItems(items);
        if (selected >= 0)
            combo.SelectIndex(selected);

        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing)
                return;

            int index = combo.SelectedIndex;
            if (index >= 0 && index < values.Count)
            {
                _formState.SetSelectedValue(id, name, values[index]);
                OptionChosen?.Invoke(this, new HtmlOptionChosenEventArgs(selectIndex, index));
            }

            Changed?.Invoke(this, EventArgs.Empty);
        };

        return combo;
    }

    /// <summary>
    /// The option a drop-down shows: the last option the markup marks, as the page's select has it (HTML's
    /// selectedness setting algorithm; measured in Chromium), or the first. On a page that keeps no state of its
    /// own, the option the user chose outranks the markup.
    /// </summary>
    private int ShownOption(DomElement select, string id, string name)
    {
        List<string> values = [];
        int selected = -1;
        foreach (DomElement option in Options(select))
        {
            if (option.HasAttribute("selected"))
                selected = values.Count;

            values.Add(option.GetAttribute("value") ?? (option.TextContent ?? string.Empty).Trim());
        }

        if (selected < 0 && values.Count > 0)
            selected = 0;

        string? recorded = _formState.GetSelectedValue(id, name);
        if (recorded is not null)
        {
            int index = values.IndexOf(recorded);
            if (index >= 0)
                selected = index;
        }

        return selected;
    }

    /// <summary>
    /// The options a multiple select's list has selected, as item ids: every option the markup marks or, on a
    /// page that keeps no state of its own, the ones the user chose.
    /// </summary>
    private List<string> ShownOptions(DomElement select, string id, string name)
    {
        IReadOnlyList<string>? recorded = _formState.GetSelectedValues(id, name);
        if (recorded is not null)
        {
            return [.. recorded.Select(value => IndexOfValue(select, value)).Where(index => index >= 0)
                .Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture))];
        }

        List<string> marked = [];
        int position = 0;
        foreach (DomElement option in Options(select))
        {
            if (option.HasAttribute("selected"))
                marked.Add(position.ToString(System.Globalization.CultureInfo.InvariantCulture));
            position++;
        }

        return marked;
    }

    /// <summary>A select's options, in tree order.</summary>
    private static IEnumerable<DomElement> Options(DomElement select) =>
        Descendants(select).Where(static element => string.Equals(element.TagName, "option", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Builds the button that stands in for an <c>&lt;input type="file"&gt;</c>. It
    /// shows the chosen file's name, or the usual prompt when nothing is chosen.
    /// </summary>
    private UiElement CreateFilePicker(string id, string name, bool allowsMultiple, int fileInputIndex)
    {
        StandardButton button = new()
        {
            Font = new BFontStyle(UaControlFontFamily, UaControlFontSize),
            CornerRadius = 0,
            PaddingX = 4,
            PaddingY = 0,
            Text = DescribeFile(id, name, allowsMultiple),
        };

        button.Clicked += (_, _) =>
            FilePickRequested?.Invoke(this, new HtmlFilePickEventArgs(id, name, allowsMultiple, fileInputIndex));
        _filePickers.Add(new FilePicker(id, name, allowsMultiple, button));
        return button;
    }

    /// <summary>
    /// Re-reads the chosen files into the hosted buttons' labels. Called once the
    /// browser has recorded a pick, since the host cannot show a file dialog itself.
    /// </summary>
    public void RefreshFileLabels()
    {
        foreach (FilePicker picker in _filePickers)
            picker.Button.Text = DescribeFile(picker.Id, picker.Name, picker.AllowsMultiple);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private string DescribeFile(string id, string name, bool allowsMultiple)
    {
        IReadOnlyList<string> paths = _formState.GetSelectedFiles(id, name);
        return paths.Count switch
        {
            0 => allowsMultiple ? NoFilesChosen : NoFileChosen,
            1 => Path.GetFileName(paths[0]),
            // A `multiple` control accumulates, so name the count rather than
            // truncating a list into a button.
            _ => $"{paths.Count} files",
        };
    }

    /// <summary>
    /// Builds the multi-selection list that stands in for a
    /// <c>&lt;select multiple&gt;</c>, seeded with every option the page marks
    /// <c>selected</c>.
    /// </summary>
    /// <remarks>
    /// Unlike a single-choice select there is no fallback to the first option: a
    /// multi-select with nothing marked genuinely has nothing selected.
    /// </remarks>
    private StandardListView CreateListView(DomElement select, string id, string name, int selectIndex)
    {
        List<UiListItem> items = [];

        foreach (DomElement option in Options(select))
        {
            string text = (option.TextContent ?? string.Empty).Trim();

            // Options can repeat a value; the list needs unique ids, so index them.
            string itemId = items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            items.Add(new UiListItem(itemId, text));
        }

        StandardListView list = new()
        {
            SelectionMode = UiListSelectionMode.Multiple,
            Font = new BFontStyle(UaControlFontFamily, UaControlFontSize),
            CornerRadius = 0,
        };
        list.SetItems(items);
        list.SetSelectedItems(ShownOptions(select, id, name));

        list.SelectionChanged += (_, _) =>
        {
            if (_syncing)
                return;

            _formState.SetSelectedValues(id, name, list.SelectedItemIds.Select(itemId => ValueAt(select, itemId)));

            // The page's select takes the whole choice, every option the user has selected.
            List<int> chosen = [];
            foreach (string itemId in list.SelectedItemIds)
            {
                if (int.TryParse(itemId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int option))
                    chosen.Add(option);
            }

            OptionsChosen?.Invoke(this, new HtmlOptionsChosenEventArgs(selectIndex, chosen));

            Changed?.Invoke(this, EventArgs.Empty);
        };

        return list;
    }

    /// <summary>The option value at a list item's index, since ids are positional.</summary>
    private static string ValueAt(DomElement select, string itemId)
    {
        if (!int.TryParse(itemId, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out int index))
        {
            return string.Empty;
        }

        int seen = 0;
        foreach (DomElement option in Descendants(select))
        {
            if (!string.Equals(option.TagName, "option", StringComparison.OrdinalIgnoreCase))
                continue;

            if (seen++ != index)
                continue;

            string text = (option.TextContent ?? string.Empty).Trim();
            return option.GetAttribute("value") ?? text;
        }

        return string.Empty;
    }

    /// <summary>The index of the first option carrying <paramref name="value"/>, or -1.</summary>
    private static int IndexOfValue(DomElement select, string value)
    {
        int index = 0;
        foreach (DomElement option in Descendants(select))
        {
            if (!string.Equals(option.TagName, "option", StringComparison.OrdinalIgnoreCase))
                continue;

            string text = (option.TextContent ?? string.Empty).Trim();
            if (string.Equals(option.GetAttribute("value") ?? text, value, StringComparison.Ordinal))
                return index;

            index++;
        }

        return -1;
    }

    /// <summary>
    /// Sizes the control's mark to the box the page laid out, so a hosted control
    /// occupies exactly the 13×13 (or author-styled) area the renderer reserved.
    /// </summary>
    private static void ApplyMarkSize(UiElement control, BRect target)
    {
        double size = Math.Max(1, Math.Min(target.Width, target.Height));
        switch (control)
        {
            case StandardCheckBox box:
                box.BoxSize = size;
                box.Spacing = 0;
                break;
            case StandardRadioButton radio:
                radio.MarkSize = size;
                radio.Spacing = 0;
                break;
            case StandardListView list:
                // A list fills its box and scrolls; its rows keep the UA line height.
                list.ItemHeight = Math.Max(1, UaControlFontSize * 1.4);
                break;
            case StandardComboBox combo:
                // A combo fills its box rather than sizing a mark inside it, and its
                // row height has to match or the closed control clips its own text.
                combo.ItemHeight = Math.Max(1, target.Height);
                break;
        }
    }

    private static RectangleF? TryGetRectangle(HtmlContainer container, string elementId)
    {
        try
        {
            return container.GetElementRectangle(elementId);
        }
        catch (Exception)
        {
            // An id the box tree no longer carries (the page was rewritten under us)
            // simply has no geometry; the control hides until it comes back.
            return null;
        }
    }

    private static DomElement? TryParse(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        try
        {
            return HtmlDocumentParser.ParseDocument(html).Document.DocumentElement;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<DomElement> Descendants(DomNode root)
    {
        foreach (DomNode child in root.ChildNodes)
        {
            if (child is DomElement element)
                yield return element;

            foreach (DomElement nested in Descendants(child))
                yield return nested;
        }
    }

    /// <param name="Shape">What makes it the same control in a later rendering of the page (<see cref="Shape(ControlSite)"/>).</param>
    /// <param name="Apply">Sets it to the state an element of a later rendering has.</param>
    private sealed record HostedToggle(string ElementId, UiElement Control, string Shape, Action<DomElement> Apply);

    /// <summary>A control of the page to host.</summary>
    /// <param name="PageIndex">For a select, its place among the page's selects; for a file input, among its file inputs.</param>
    private readonly record struct ControlSite(DomElement Element, string Id, bool IsSelect, bool IsRadio, bool IsFile, int PageIndex);

    /// <summary>Identifies a toggle for <see cref="HtmlFormState"/>.</summary>
    private sealed record ToggleIdentity(string Id, string Name, string Value);

    /// <summary>A hosted file control and the button standing in for it.</summary>
    private sealed record FilePicker(string Id, string Name, bool AllowsMultiple, StandardButton Button);
}

/// <summary>Identifies the file control the user activated.</summary>
internal sealed class HtmlFilePickEventArgs(string controlId, string controlName, bool allowsMultiple, int fileInputIndex = -1) : EventArgs
{
    public string ControlId { get; } = controlId;

    public string ControlName { get; } = controlName;

    /// <summary>Whether the control accepts more than one file, so picks accumulate.</summary>
    public bool AllowsMultiple { get; } = allowsMultiple;

    /// <summary>The input's place among the page's file inputs, counted in tree order; -1 when unknown.</summary>
    public int FileInputIndex { get; } = fileInputIndex;
}

/// <summary>The user chose exactly the options <see cref="OptionIndexes"/> of the page's multiple select <see cref="SelectIndex"/>, counted in tree order.</summary>
internal sealed class HtmlOptionsChosenEventArgs(int selectIndex, IReadOnlyList<int> optionIndexes) : EventArgs
{
    public int SelectIndex { get; } = selectIndex;

    public IReadOnlyList<int> OptionIndexes { get; } = optionIndexes;
}

/// <summary>The user chose option <see cref="OptionIndex"/> of the page's select <see cref="SelectIndex"/>, both counted in tree order.</summary>
internal sealed class HtmlOptionChosenEventArgs(int selectIndex, int optionIndex) : EventArgs
{
    public int SelectIndex { get; } = selectIndex;

    public int OptionIndex { get; } = optionIndex;
}
