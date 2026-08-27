using System.Drawing;
using Broiler.Browser;
using Broiler.Graphics;
using Broiler.HtmlBridge;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.CheckBox.Standard;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel.Standard;
using Broiler.UI.RadioButton.Standard;
using HtmlContainer = Broiler.HTML.Image.HtmlContainer;

namespace Broiler.Browser.Core.Tests;

/// <summary>
/// Covers the Broiler.UI controls hosted over the page's checkboxes and radios.
/// The renderer draws them as empty 13×13 bordered boxes — no check, no dot, no way
/// to toggle — so the hosted controls are what gives them state at all.
/// </summary>
public class HtmlFormControlHostTests
{
    private const string TogglePage =
        "<html><body style='margin:0'><form action='/s'>" +
        "<input type='checkbox' name='a' value='1'>" +
        "<input type='checkbox' name='b' value='2' checked>" +
        "<input type='radio' name='c' value='x'>" +
        "<input type='radio' name='c' value='y' checked>" +
        "</form></body></html>";

    private static HtmlContainer LayOut(string html, int width = 600, int height = 200)
    {
        HtmlContainer container = new()
        {
            AvoidAsyncImagesLoading = true,
            AvoidImagesLateLoading = true,
            Location = PointF.Empty,
            MaxSize = new SizeF(width, height),
        };

        // The browsing path stamps ids so the controls can be located by geometry.
        container.SetHtmlWithStyleSet(HtmlPostProcessor.StampFormControlIds(html));
        container.PerformLayout(new RectangleF(0, 0, width, height));
        return container;
    }

    /// <summary>
    /// Hosts inside a real session: radio-group exclusivity resolves peers by walking
    /// the session's roots, so a detached owner would leave both radios checked.
    /// </summary>
    private static (HtmlFormControlHost Host, HtmlFormState State, StandardPanel Owner) Create(TestUiSession session)
    {
        HtmlFormState state = new();
        return (new HtmlFormControlHost(session.Root, state), state, session.Root);
    }

    [Fact(Timeout = 600000)]
    public void EveryCheckboxAndRadioGetsAHostedControl()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(TogglePage);
        (HtmlFormControlHost host, _, _) = Create(session);

        host.Rebuild(container.GetHtml());

        Assert.Equal(4, host.Count);
        Assert.Equal(2, host.Controls.Count(c => c is StandardCheckBox));
        Assert.Equal(2, host.Controls.Count(c => c is StandardRadioButton));
    }

    [Fact(Timeout = 600000)]
    public void HostedControlsStartFromTheMarkupsCheckedState()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(TogglePage);
        (HtmlFormControlHost host, _, _) = Create(session);

        host.Rebuild(container.GetHtml());

        List<StandardCheckBox> boxes = host.Controls.OfType<StandardCheckBox>().ToList();
        Assert.False(boxes[0].IsChecked);
        Assert.True(boxes[1].IsChecked);

        List<StandardRadioButton> radios = host.Controls.OfType<StandardRadioButton>().ToList();
        Assert.False(radios[0].IsChecked);
        Assert.True(radios[1].IsChecked);
    }

    [Fact(Timeout = 600000)]
    public void HostedControlsArePlacedOverTheBoxesTheRendererLaidOut()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(TogglePage);
        (HtmlFormControlHost host, _, _) = Create(session);
        host.Rebuild(container.GetHtml());

        BRect viewport = new(10, 20, 600, 200);
        host.UpdateViewport(container, viewport, zoom: 1, scrollY: 0);

        foreach (UiElement control in host.Controls)
        {
            Assert.Equal(UiVisibility.Visible, control.Visibility);
            Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
            // Inside the viewport, and offset by its origin.
            Assert.True(control.Bounds.Left >= viewport.Left);
            Assert.True(control.Bounds.Top >= viewport.Top);
        }

        // The four controls sit on one line, so no two share a left edge.
        List<double> lefts = host.Controls.Select(c => c.Bounds.Left).ToList();
        Assert.Equal(lefts.Count, lefts.Distinct().Count());
    }

    [Fact(Timeout = 600000)]
    public void HostedControlsFollowScrollingAndHideWhenOutOfView()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(TogglePage);
        (HtmlFormControlHost host, _, _) = Create(session);
        host.Rebuild(container.GetHtml());

        BRect viewport = new(0, 0, 600, 200);
        host.UpdateViewport(container, viewport, zoom: 1, scrollY: 0);
        double top = host.Controls[0].Bounds.Top;

        host.UpdateViewport(container, viewport, zoom: 1, scrollY: 10);
        Assert.Equal(top - 10, host.Controls[0].Bounds.Top, 3);

        host.UpdateViewport(container, viewport, zoom: 1, scrollY: 5000);
        Assert.All(host.Controls, c => Assert.Equal(UiVisibility.Collapsed, c.Visibility));
    }

    [Fact(Timeout = 600000)]
    public void TogglingACheckboxRecordsItForSubmission()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(TogglePage);
        (HtmlFormControlHost host, HtmlFormState state, _) = Create(session);
        host.Rebuild(container.GetHtml());

        StandardCheckBox first = host.Controls.OfType<StandardCheckBox>().First();
        int toggled = 0;
        host.Changed += (_, _) => toggled++;

        Assert.True(first.Toggle());

        Assert.Equal(1, toggled);
        Assert.True(state.GetChecked($"{HtmlPostProcessor.SyntheticIdPrefix}0", "a", "1"));
    }

    [Fact(Timeout = 600000)]
    public void SelectingARadioClearsTheOtherInItsGroup()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(TogglePage);
        (HtmlFormControlHost host, HtmlFormState state, _) = Create(session);
        host.Rebuild(container.GetHtml());

        List<StandardRadioButton> radios = host.Controls.OfType<StandardRadioButton>().ToList();
        Assert.True(radios[0].Select());

        // The group scope drives the UI, and the recorded state follows it.
        Assert.True(radios[0].IsChecked);
        Assert.False(radios[1].IsChecked);
        Assert.True(state.GetChecked($"{HtmlPostProcessor.SyntheticIdPrefix}2", "c", "x"));
        Assert.False(state.GetChecked($"{HtmlPostProcessor.SyntheticIdPrefix}3", "c", "y"));
    }

    [Fact(Timeout = 600000)]
    public void ClearRemovesEveryHostedControlFromTheOwner()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(TogglePage);
        (HtmlFormControlHost host, _, StandardPanel owner) = Create(session);
        host.Rebuild(container.GetHtml());
        Assert.Equal(4, owner.Children.Count);

        host.Clear();

        Assert.Equal(0, host.Count);
        Assert.Empty(owner.Children);
    }

    [Fact(Timeout = 600000)]
    public void RebuildingReplacesThePreviousPagesControls()
    {
        using TestUiSession session = new();
        using HtmlContainer first = LayOut(TogglePage);
        using HtmlContainer second = LayOut(
            "<html><body><input type='checkbox' name='only'></body></html>");
        (HtmlFormControlHost host, _, StandardPanel owner) = Create(session);

        host.Rebuild(first.GetHtml());
        host.Rebuild(second.GetHtml());

        Assert.Equal(1, host.Count);
        Assert.Single(owner.Children);
    }

    [Fact(Timeout = 600000)]
    public void PagesWithoutTogglesHostNothing()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(
            "<html><body><form><input type='text' name='q'>" +
            "<input type='submit' value='Go'></form></body></html>");
        (HtmlFormControlHost host, _, _) = Create(session);

        host.Rebuild(container.GetHtml());

        Assert.Equal(0, host.Count);
    }

    [Fact(Timeout = 600000)]
    public void UnparseableOrEmptyPagesHostNothingInsteadOfThrowing()
    {
        using TestUiSession session = new();
        (HtmlFormControlHost host, _, _) = Create(session);

        host.Rebuild(string.Empty);
        Assert.Equal(0, host.Count);

        host.UpdateViewport(null, new BRect(0, 0, 100, 100), zoom: 1, scrollY: 0);
        Assert.Equal(0, host.Count);
    }

    private const string SelectPage =
        "<html><body style='margin:0'><form action='/s'>" +
        "<select name='colour'><option value='r'>Red</option>" +
        "<option value='g' selected>Green</option><option value='b'>Blue</option></select>" +
        "</form></body></html>";

    [Fact(Timeout = 600000)]
    public void SelectsGetAHostedComboBoxCarryingTheirOptions()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(SelectPage);
        (HtmlFormControlHost host, _, _) = Create(session);

        host.Rebuild(container.GetHtml());

        StandardComboBox combo = Assert.IsType<StandardComboBox>(Assert.Single(host.Controls));
        Assert.Equal(["Red", "Green", "Blue"], combo.Items.Select(i => i.Text));
        Assert.Equal(["r", "g", "b"], combo.Items.Select(i => i.Id));
    }

    [Fact(Timeout = 600000)]
    public void HostedComboBoxStartsOnTheMarkupsSelectedOption()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(SelectPage);
        (HtmlFormControlHost host, _, _) = Create(session);

        host.Rebuild(container.GetHtml());

        StandardComboBox combo = host.Controls.OfType<StandardComboBox>().Single();
        Assert.Equal(1, combo.SelectedIndex);
        Assert.Equal("g", combo.SelectedItem?.Id);
    }

    [Fact(Timeout = 600000)]
    public void ASelectWithNothingMarkedStartsOnItsFirstOption()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(
            "<html><body><form><select name='s'><option value='a'>A</option>" +
            "<option value='b'>B</option></select></form></body></html>");
        (HtmlFormControlHost host, _, _) = Create(session);

        host.Rebuild(container.GetHtml());

        Assert.Equal(0, host.Controls.OfType<StandardComboBox>().Single().SelectedIndex);
    }

    [Fact(Timeout = 600000)]
    public void OptionsWithoutAValueSubmitTheirText()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(
            "<html><body><form><select name='s'><option>Plain</option></select></form></body></html>");
        (HtmlFormControlHost host, _, _) = Create(session);

        host.Rebuild(container.GetHtml());

        Assert.Equal("Plain", host.Controls.OfType<StandardComboBox>().Single().SelectedItem?.Id);
    }

    [Fact(Timeout = 600000)]
    public void ChangingTheSelectionRecordsItForSubmission()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(SelectPage);
        (HtmlFormControlHost host, HtmlFormState state, _) = Create(session);
        host.Rebuild(container.GetHtml());
        int changed = 0;
        host.Changed += (_, _) => changed++;

        StandardComboBox combo = host.Controls.OfType<StandardComboBox>().Single();
        Assert.True(combo.SelectIndex(2));

        Assert.Equal(1, changed);
        Assert.Equal("b", state.GetSelectedValue($"{HtmlPostProcessor.SyntheticIdPrefix}0", "colour"));
    }

    private const string MultiSelectPage =
        "<html><body style='margin:0'><form action='/s'>" +
        "<select name='colours' multiple><option value='r'>Red</option>" +
        "<option value='g' selected>Green</option><option value='b' selected>Blue</option></select>" +
        "</form></body></html>";

    [Fact(Timeout = 600000)]
    public void AMultiSelectIsHostedAsAMultiSelectionListView()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(MultiSelectPage);
        (HtmlFormControlHost host, _, _) = Create(session);

        host.Rebuild(container.GetHtml());

        StandardListView list = Assert.IsType<StandardListView>(Assert.Single(host.Controls));
        Assert.Equal(UiListSelectionMode.Multiple, list.SelectionMode);
        Assert.Equal(["Red", "Green", "Blue"], list.Items.Select(i => i.Text));
    }

    [Fact(Timeout = 600000)]
    public void AMultiSelectStartsOnEveryOptionTheMarkupMarks()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(MultiSelectPage);
        (HtmlFormControlHost host, _, _) = Create(session);

        host.Rebuild(container.GetHtml());

        StandardListView list = host.Controls.OfType<StandardListView>().Single();
        // Options 1 and 2 are the marked ones; ids are positional.
        Assert.Equal(["1", "2"], list.SelectedItemIds);
    }

    [Fact(Timeout = 600000)]
    public void AMultiSelectWithNothingMarkedStartsEmpty()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(
            "<html><body><form><select name='s' multiple><option value='a'>A</option>" +
            "<option value='b'>B</option></select></form></body></html>");
        (HtmlFormControlHost host, _, _) = Create(session);

        host.Rebuild(container.GetHtml());

        // Unlike a single-choice select, there is no fallback to the first option.
        Assert.Empty(host.Controls.OfType<StandardListView>().Single().SelectedItemIds);
    }

    [Fact(Timeout = 600000)]
    public void ChangingAMultiSelectRecordsEveryChosenValue()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(MultiSelectPage);
        (HtmlFormControlHost host, HtmlFormState state, _) = Create(session);
        host.Rebuild(container.GetHtml());

        StandardListView list = host.Controls.OfType<StandardListView>().Single();
        list.SetSelectedItems(["0", "2"]);

        Assert.Equal(
            ["r", "b"],
            state.GetSelectedValues($"{HtmlPostProcessor.SyntheticIdPrefix}0", "colours"));
    }

    [Fact(Timeout = 600000)]
    public void DeselectingEverythingInAMultiSelectIsRecordedAsEmptyNotAsUntouched()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(MultiSelectPage);
        (HtmlFormControlHost host, HtmlFormState state, _) = Create(session);
        host.Rebuild(container.GetHtml());

        host.Controls.OfType<StandardListView>().Single().SetSelectedItems([]);

        // Empty is a real state; null would mean "fall back to the markup".
        Assert.Empty(state.GetSelectedValues($"{HtmlPostProcessor.SyntheticIdPrefix}0", "colours")!);
    }

    [Fact(Timeout = 600000)]
    public void HostedComboBoxIsPlacedOverTheSelectTheRendererLaidOut()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(SelectPage);
        (HtmlFormControlHost host, _, _) = Create(session);
        host.Rebuild(container.GetHtml());

        BRect viewport = new(10, 20, 600, 200);
        host.UpdateViewport(container, viewport, zoom: 1, scrollY: 0);

        UiElement combo = host.Controls.Single();
        Assert.Equal(UiVisibility.Visible, combo.Visibility);
        Assert.True(combo.Bounds.Width > 0 && combo.Bounds.Height > 0);
        Assert.True(combo.Bounds.Left >= viewport.Left);
    }

    [Fact(Timeout = 600000)]
    public void FileControlsGetAHostedButtonThatAsksTheShellToPick()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(
            "<html><body><form><input type='file' name='doc'></form></body></html>");
        (HtmlFormControlHost host, _, _) = Create(session);
        host.Rebuild(container.GetHtml());

        StandardButton button = Assert.IsType<StandardButton>(Assert.Single(host.Controls));
        Assert.Equal("Choose File", button.Text);

        HtmlFilePickEventArgs? asked = null;
        host.FilePickRequested += (_, e) => asked = e;
        button.Click();

        Assert.NotNull(asked);
        Assert.Equal($"{HtmlPostProcessor.SyntheticIdPrefix}0", asked.ControlId);
        Assert.Equal("doc", asked.ControlName);
    }

    [Fact(Timeout = 600000)]
    public void AChosenFilesNameShowsOnTheHostedButton()
    {
        using TestUiSession session = new();
        using HtmlContainer container = LayOut(
            "<html><body><form><input type='file' name='doc'></form></body></html>");
        (HtmlFormControlHost host, HtmlFormState state, _) = Create(session);
        host.Rebuild(container.GetHtml());

        state.SetSelectedFile($"{HtmlPostProcessor.SyntheticIdPrefix}0", "doc", "/tmp/report.pdf");
        host.RefreshFileLabels();

        Assert.Equal("report.pdf", ((StandardButton)host.Controls[0]).Text);
    }
}
