using Broiler.CSS;
using Broiler.Layout.Diagnostics;
using System.Drawing;


namespace Broiler.Layout.Engine;

internal sealed class CssLayoutEngineTable
{
    private readonly CssBox _tableBox;

    private CssBox _headerBox;
    private CssBox _footerBox;

    private readonly List<CssBox> _bodyrows = [];
    private readonly List<CssBox> _columns = [];
    private readonly List<CssBox> _allRows = [];

    // CSS2.1 §17.4.1: table-caption boxes are laid out as block boxes above
    // (caption-side:top, the default) or below the table box, spanning the
    // table's width. Broiler previously dropped them entirely, so caption text
    // never rendered; collect them here to lay out in LayoutCells.
    private readonly List<CssBox> _captions = [];

    private int _columnCount;

    private bool _widthSpecified;

    private double[] _columnWidths;
    private double[] _columnMinWidths;

    private CssLayoutEngineTable(CssBox tableBox) => _tableBox = tableBox;

    public static double GetTableSpacing(CssBox tableBox)
    {
        int count = 0;
        int columns = 0;

        foreach (var box in tableBox.Boxes)
        {
            if (box.Display == CssConstants.TableColumn)
            {
                columns += GetSpan(box);
            }
            else if (box.Display == CssConstants.TableRowGroup)
            {
                foreach (CssBox cr in tableBox.Boxes)
                {
                    count++;
                    if (cr.Display == CssConstants.TableRow)
                        columns = Math.Max(columns, cr.Boxes.Count);
                }
            }
            else if (box.Display == CssConstants.TableRow)
            {
                count++;
                columns = Math.Max(columns, box.Boxes.Count);
            }

            // limit the amount of rows to process for performance
            if (count > 30)
                break;
        }

        // +1 columns because padding is between the cell and table borders
        return (columns + 1) * GetHorizontalSpacing(tableBox);
    }

    public static void PerformLayout(ILayoutEnvironment g, CssBox tableBox, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(tableBox);

        using var trace = LayoutWorkTrace.Measure(LayoutWorkTrace.Ops.Table);

        try
        {
            var table = new CssLayoutEngineTable(tableBox);
            table.Layout(g, baseUrl);
        }
        catch (Exception ex)
        {
            tableBox.LayoutEnvironment.ReportLayoutError("Failed table layout", ex);
        }
    }


    private void Layout(ILayoutEnvironment g, Uri baseUrl)
    {
        MeasureWords(_tableBox, g);

        // get the table boxes into the proper fields
        AssignBoxKinds(baseUrl);

        // Insert EmptyBoxes for vertical cell spanning.
        InsertEmptyBoxes(baseUrl);

        // CSS2.1 §17.6.2.1: resolve conflicting borders at each shared cell edge
        // before sizing, so the used (winning) border widths drive cell layout.
        ResolveCollapsedBorders();

        // Determine Row and Column Count, and ColumnWidths
        var availCellSpace = CalculateCountAndWidth();

        DetermineMissingColumnWidths(availCellSpace);

        // Check for minimum sizes (increment widths if necessary)
        EnforceMinimumSize();

        // While table width is larger than it should, and width is reducible
        EnforceMaximumSize();

        // Ensure there's no padding
        _tableBox.PaddingLeft = _tableBox.PaddingTop = _tableBox.PaddingRight = _tableBox.PaddingBottom = "0";

        //Actually layout cells!
        LayoutCells(g);
    }

    private void AssignBoxKinds(Uri baseUrl)
    {
        // CSS2.1 §17.2.1: Generate anonymous table-row boxes for table-cell
        // children that are direct children of the table without an
        // intermediate table-row wrapper.
        GenerateAnonymousTableRows(baseUrl);
        GenerateAnonymousRowsInRowGroups(baseUrl);

        foreach (var box in _tableBox.Boxes)
        {
            switch (box.Display)
            {
                case CssConstants.TableCaption:
                    _captions.Add(box);
                    break;

                case CssConstants.TableRow:
                    _bodyrows.Add(box);
                    break;

                case CssConstants.TableRowGroup:
                    foreach (CssBox childBox in box.Boxes)
                        if (childBox.Display == CssConstants.TableRow)
                            _bodyrows.Add(childBox);
                    break;
                
                case CssConstants.TableHeaderGroup:
                    if (_headerBox != null)
                        _bodyrows.Add(box);
                    else
                        _headerBox = box;
                    break;
                
                case CssConstants.TableFooterGroup:
                    if (_footerBox != null)
                        _bodyrows.Add(box);
                    else
                        _footerBox = box;
                    break;
                
                case CssConstants.TableColumn:
                    for (int i = 0; i < GetSpan(box); i++)
                        _columns.Add(box);
                    break;
                
                case CssConstants.TableColumnGroup:
                    if (box.Boxes.Count == 0)
                    {
                        int gspan = GetSpan(box);
                        for (int i = 0; i < gspan; i++)
                        {
                            _columns.Add(box);
                        }
                    }
                    else
                    {
                        foreach (CssBox bb in box.Boxes)
                        {
                            int bbspan = GetSpan(bb);
                            for (int i = 0; i < bbspan; i++)
                            {
                                _columns.Add(bb);
                            }
                        }
                    }
                    break;
            }
        }

        if (_headerBox != null)
            _allRows.AddRange(_headerBox.Boxes);

        _allRows.AddRange(_bodyrows);

        if (_footerBox != null)
            _allRows.AddRange(_footerBox.Boxes);

        // CSS2.1 §17.2.1: within each row, a child that is not a table-cell (a
        // block, inline, etc.) is wrapped — together with its consecutive
        // non-cell siblings — in an anonymous table-cell box. GenerateAnonymousTableRows
        // only wraps non-cell children of the *table*; a non-cell child of an
        // explicit <div style="display:table-row"> was left uncelled and never laid
        // out (WPT css-sizing/table-child-percentage-height-with-border-box).
        foreach (var row in _allRows)
            WrapRowNonCellChildrenInAnonymousCells(row, baseUrl);
    }

    /// <summary>
    /// CSS2.1 §17.2.1: wraps each run of consecutive non-<c>table-cell</c> children of
    /// <paramref name="row"/> in a single anonymous <c>table-cell</c> box, preserving
    /// child order. A no-op when every child is already a table-cell.
    /// </summary>
    private void WrapRowNonCellChildrenInAnonymousCells(CssBox row, Uri baseUrl)
    {
        bool needsWrapping = false;
        foreach (var child in row.Boxes)
        {
            if (child.Display != CssConstants.TableCell)
            {
                needsWrapping = true;
                break;
            }
        }

        if (!needsWrapping)
            return;

        var children = new List<CssBox>(row.Boxes);
        row.Boxes.Clear();

        List<CssBox>? pendingNonCell = null;

        void FlushAnonymousCell()
        {
            if (pendingNonCell == null)
                return;

            // The CssBox(parent, tag) constructor appends the new cell to row.Boxes,
            // keeping it in order after any real cells already re-added.
            var anonCell = new CssBox(row, null, baseUrl) { Display = CssConstants.TableCell };
            foreach (var child in pendingNonCell)
                child.ParentBox = anonCell;
            pendingNonCell = null;
        }

        foreach (var child in children)
        {
            if (child.Display == CssConstants.TableCell)
            {
                FlushAnonymousCell();
                row.Boxes.Add(child);
            }
            else
            {
                pendingNonCell ??= [];
                pendingNonCell.Add(child);
            }
        }

        FlushAnonymousCell();
    }

    /// <summary>
    /// CSS2.1 §17.2.1: Generate anonymous table-row boxes for children of a
    /// table element that are not proper table sub-elements (table-row,
    /// table-row-group, table-header-group, table-footer-group, table-caption,
    /// table-column, or table-column-group).  All consecutive non-row children
    /// are wrapped together in a single anonymous table-row.  Within each
    /// anonymous row, children that are not table-cell are additionally wrapped
    /// in anonymous table-cell boxes.
    /// </summary>
    private void GenerateAnonymousTableRows(Uri baseUrl)
    {
        bool needsWrapping = false;
        
        foreach (var box in _tableBox.Boxes)
        {
            if (!IsProperTableChild(box.Display))
            {
                needsWrapping = true;
                break;
            }
        }

        if (!needsWrapping)
            return;

        // Collect children and group consecutive non-row children into
        // anonymous table-row wrappers.
        var children = new List<CssBox>(_tableBox.Boxes);
        _tableBox.Boxes.Clear();

        List<CssBox>? pendingNonRow = null;

        foreach (var child in children)
        {
            if (IsProperTableChild(child.Display))
            {
                if (pendingNonRow != null)
                {
                    FlushAnonymousRow(pendingNonRow, baseUrl);
                    pendingNonRow = null;
                }

                _tableBox.Boxes.Add(child);
            }
            else
            {
                pendingNonRow ??= [];
                pendingNonRow.Add(child);
            }
        }

        if (pendingNonRow != null)
            FlushAnonymousRow(pendingNonRow, baseUrl);
    }

    /// <summary>
    /// Returns true if the display value is a proper direct child of a table
    /// element per CSS2.1 §17.2.1 (table-row, row-group, caption, column, etc.).
    /// </summary>
    private static bool IsProperTableChild(string display)
    {
        return display == CssConstants.TableRow
            || display == CssConstants.TableRowGroup
            || display == CssConstants.TableHeaderGroup
            || display == CssConstants.TableFooterGroup
            || display == CssConstants.TableCaption
            || display == CssConstants.TableColumn
            || display == CssConstants.TableColumnGroup;
    }

    /// <summary>
    /// Creates an anonymous table-row box, re-parents the given children into
    /// it, and appends the row to the table.  Children that are not table-cell
    /// are additionally wrapped in anonymous table-cell boxes (CSS2.1 §17.2.1).
    /// </summary>
    /// <summary>
    /// CSS2.1 §17.2.1, the same rule <see cref="GenerateAnonymousTableRows"/> applies one level
    /// up: a row group may contain only rows, so a run of non-row children has to be wrapped in
    /// an anonymous <c>table-row</c> (and, inside it, anonymous cells).
    /// <para>Without this the collection loop below — which takes a row group's children only when
    /// their display is <c>table-row</c> — silently dropped every other child, so it never laid
    /// out and never painted, and its height did not reach the table. WPT
    /// css/css-page/monolithic-overflow-011-print puts a 350vh block straight inside a
    /// <c>table-row-group</c>: the block vanished and the table collapsed to its text line.</para>
    /// </summary>
    private void GenerateAnonymousRowsInRowGroups(Uri baseUrl)
    {
        foreach (var group in _tableBox.Boxes)
        {
            if (group.Display != CssConstants.TableRowGroup
                && group.Display != CssConstants.TableHeaderGroup
                && group.Display != CssConstants.TableFooterGroup)
                continue;

            bool needsWrapping = false;
            foreach (var child in group.Boxes)
            {
                if (child.Display != CssConstants.TableRow)
                {
                    needsWrapping = true;
                    break;
                }
            }

            if (!needsWrapping)
                continue;

            var children = new List<CssBox>(group.Boxes);
            group.Boxes.Clear();

            List<CssBox>? pendingNonRow = null;

            foreach (var child in children)
            {
                if (child.Display == CssConstants.TableRow)
                {
                    if (pendingNonRow != null)
                    {
                        FlushAnonymousRow(pendingNonRow, baseUrl, group);
                        pendingNonRow = null;
                    }

                    group.Boxes.Add(child);
                }
                else
                {
                    pendingNonRow ??= [];
                    pendingNonRow.Add(child);
                }
            }

            if (pendingNonRow != null)
                FlushAnonymousRow(pendingNonRow, baseUrl, group);
        }
    }

    private void FlushAnonymousRow(List<CssBox> children, Uri baseUrl) =>
        FlushAnonymousRow(children, baseUrl, _tableBox);

    private void FlushAnonymousRow(List<CssBox> children, Uri baseUrl, CssBox container)
    {
        // Create the anonymous row. The CssBox(parent, tag) constructor
        // automatically adds the new box to parent.Boxes.
        var anonRow = new CssBox(container, null, baseUrl) { Display = CssConstants.TableRow };
        
        foreach (var child in children)
        {
            if (child.Display == CssConstants.TableCell)
            {
                // Already a table-cell — re-parent into the anonymous row.
                // Using the ParentBox setter updates _parentBox and adds
                // the child to anonRow.Boxes.
                child.ParentBox = anonRow;
            }
            else
            {
                // CSS2.1 §17.2.1: Wrap non-cell children in an anonymous
                // table-cell box.  The CssBox constructor automatically adds
                // the anonymous cell to anonRow.Boxes.
                var anonCell = new CssBox(anonRow, null, baseUrl) { Display = CssConstants.TableCell };
                child.ParentBox = anonCell;
            }
        }
    }

    private void InsertEmptyBoxes(Uri baseUrl)
    {
        if (_tableBox._tableFixed)
            return;

        int currow = 0;
        List<CssBox> rows = _bodyrows;

        foreach (CssBox row in rows)
        {
            for (int k = 0; k < row.Boxes.Count; k++)
            {
                CssBox cell = row.Boxes[k];
                int rowspan = GetRowSpan(cell);
                int realcol = GetCellRealColumnIndex(row, cell); //Real column of the cell

                for (int i = currow + 1; i < currow + rowspan; i++)
                {
                    if (rows.Count <= i)
                        continue;

                    int colcount = 0;
                    for (int j = 0; j < rows[i].Boxes.Count; j++)
                    {
                        if (colcount == realcol)
                        {
                            rows[i].Boxes.Insert(colcount, new CssSpacingBox(_tableBox, ref cell, currow, baseUrl));
                            break;
                        }

                        colcount++;
                        realcol -= GetColSpan(rows[i].Boxes[j]) - 1;
                    }
                }
            }

            currow++;
        }

        _tableBox._tableFixed = true;
    }

    private double CalculateCountAndWidth()
    {
        // Columns. Count the effective grid columns, not just the number of
        // physical cell boxes, because a one-cell row can span several columns.
        _columnCount = _columns.Count;
        foreach (CssBox row in _allRows)
            _columnCount = Math.Max(_columnCount, GetColumnSpanCount(row));

        //Initialize column widths array with NaNs
        _columnWidths = new double[_columnCount];
        for (int i = 0; i < _columnWidths.Length; i++)
            _columnWidths[i] = double.NaN;

        double availCellSpace = GetAvailableCellWidth();

        if (_columns.Count > 0)
        {
            // Fill ColumnWidths array by scanning column widths
            for (int i = 0; i < _columns.Count; i++)
            {
                CssLength len = new(_columns[i].Width); //Get specified width

                if (len.Number <= 0) //If some width specified
                    continue;

                if (len.IsPercentage) //Get width as a percentage
                {
                    // len.Number already holds the percentage as a fraction (parsed
                    // against a 100%-basis of 1), so scale it here instead of
                    // re-parsing the same string via ParseNumber.
                    _columnWidths[i] = len.Number * availCellSpace;
                }
                else if (len.Unit == CssUnit.Px || len.Unit == CssUnit.None)
                {
                    _columnWidths[i] = len.Number; //Get width as an absolute-pixel value
                }
            }
        }
        else
        {
            // Fill ColumnWidths array by scanning width in table-cell definitions
            foreach (CssBox row in _allRows)
            {
                //Check for column width in table-cell definitions
                int columnIndex = 0;
                foreach (CssBox cell in row.Boxes)
                {
                    int colspan = GetColSpan(cell);
                    int endColumn = Math.Min(_columnWidths.Length, columnIndex + colspan);
                    if (columnIndex >= _columnWidths.Length)
                        break;

                    if (cell.Display != CssConstants.TableCell)
                    {
                        columnIndex += colspan;
                        continue;
                    }

                    if (columnIndex >= 20)
                    {
                        bool spanAlreadyResolved = true;
                        for (int j = columnIndex; j < endColumn; j++)
                        {
                            if (double.IsNaN(_columnWidths[j]))
                            {
                                spanAlreadyResolved = false;
                                break;
                            }
                        }

                        if (spanAlreadyResolved)
                        {
                            columnIndex += colspan;
                            continue;
                        }
                    }

                    double len = CssLengthParser.ParseLength(cell.Width, availCellSpace, cell.GetEmHeight());

                    if (len <= 0) //If some width specified
                    {
                        columnIndex += colspan;
                        continue;
                    }

                    len /= Convert.ToSingle(colspan);

                    for (int j = columnIndex; j < endColumn; j++)
                        _columnWidths[j] = double.IsNaN(_columnWidths[j]) ? len : Math.Max(_columnWidths[j], len);

                    columnIndex += colspan;
                }
            }
        }

        return availCellSpace;
    }

    private static int GetColumnSpanCount(CssBox row)
    {
        int count = 0;
        
        foreach (CssBox cell in row.Boxes)
            count += GetColSpan(cell);
        
        return count;
    }

    private void DetermineMissingColumnWidths(double availCellSpace)
    {
        double occupedSpace = 0f;

        if (_widthSpecified) //If a width was specified,
        {
            //Assign NaNs equally with space left after gathering not-NaNs
            int numOfNans = 0;

            //Calculate number of NaNs and occupied space
            foreach (double colWidth in _columnWidths)
            {
                if (double.IsNaN(colWidth))
                    numOfNans++;
                else
                    occupedSpace += colWidth;
            }

            var orgNumOfNans = numOfNans;
            double[] orgColWidths = null;

            if (numOfNans < _columnWidths.Length)
            {
                orgColWidths = new double[_columnWidths.Length];
                
                for (int i = 0; i < _columnWidths.Length; i++)
                    orgColWidths[i] = _columnWidths[i];
            }

            if (numOfNans > 0)
            {
                // Determine the max width for each column
                GetColumnsMinMaxWidthByContent(true, out _, out double[] maxFullWidths);

                // set the columns that can fulfill by the max width in a loop because it changes the nanWidth
                int oldNumOfNans;
                do
                {
                    oldNumOfNans = numOfNans;

                    for (int i = 0; i < _columnWidths.Length; i++)
                    {
                        var nanWidth = (availCellSpace - occupedSpace) / numOfNans;
                        if (double.IsNaN(_columnWidths[i]) && nanWidth > maxFullWidths[i])
                        {
                            _columnWidths[i] = maxFullWidths[i];
                            numOfNans--;
                            occupedSpace += maxFullWidths[i];
                        }
                    }
                } while (oldNumOfNans != numOfNans);

                if (numOfNans > 0)
                {
                    // Determine width that will be assigned to un assigned widths
                    double nanWidth = (availCellSpace - occupedSpace) / numOfNans;

                    for (int i = 0; i < _columnWidths.Length; i++)
                    {
                        if (double.IsNaN(_columnWidths[i]))
                            _columnWidths[i] = nanWidth;
                    }
                }
            }

            if (numOfNans == 0 && occupedSpace < availCellSpace)
            {
                if (orgNumOfNans > 0)
                {
                    // spread extra width between all non width specified columns
                    double extWidth = (availCellSpace - occupedSpace) / orgNumOfNans;
                    for (int i = 0; i < _columnWidths.Length; i++)
                        if (orgColWidths == null || double.IsNaN(orgColWidths[i]))
                            _columnWidths[i] += extWidth;
                }
                else
                {
                    // spread extra width between all columns with respect to relative sizes
                    for (int i = 0; i < _columnWidths.Length; i++)
                        _columnWidths[i] += (availCellSpace - occupedSpace) * (_columnWidths[i] / occupedSpace);
                }
            }
        }
        else
        {
            //Get the minimum and maximum full length of NaN boxes
            GetColumnsMinMaxWidthByContent(true, out double[] minFullWidths, out double[] maxFullWidths);

            for (int i = 0; i < _columnWidths.Length; i++)
            {
                if (double.IsNaN(_columnWidths[i]))
                    _columnWidths[i] = minFullWidths[i];
                occupedSpace += _columnWidths[i];
            }

            // spread extra width between all columns
            for (int i = 0; i < _columnWidths.Length; i++)
            {
                if (maxFullWidths[i] > _columnWidths[i])
                {
                    var temp = _columnWidths[i];
                    _columnWidths[i] = Math.Min(_columnWidths[i] + (availCellSpace - occupedSpace) / Convert.ToSingle(_columnWidths.Length - i), maxFullWidths[i]);
                    occupedSpace = occupedSpace + _columnWidths[i] - temp;
                }
            }
        }
    }

    private void EnforceMaximumSize()
    {
        int curCol = 0;
        var widthSum = GetWidthSum();
        
        while (widthSum > GetAvailableTableWidth() && CanReduceWidth())
        {
            while (!CanReduceWidth(curCol))
                curCol++;

            _columnWidths[curCol] -= 1f;

            curCol++;

            if (curCol >= _columnWidths.Length)
                curCol = 0;
        }

        // if table max width is limited by we need to lower the columns width even if it will result in clipping
        var maxWidth = GetMaxTableWidth();
        if (maxWidth >= 90999)
            return;

        widthSum = GetWidthSum();

        if (maxWidth >= widthSum)
            return;

        //Get the minimum and maximum full length of NaN boxes
        GetColumnsMinMaxWidthByContent(false, out double[] minFullWidths, out double[] maxFullWidths);

        // lower all the columns to the minimum
        for (int i = 0; i < _columnWidths.Length; i++)
            _columnWidths[i] = minFullWidths[i];

        // either min for all column is not enought and we need to lower it more resulting in clipping
        // or we now have extra space so we can give it to columns than need it
        widthSum = GetWidthSum();

        if (maxWidth < widthSum)
        {
            // lower the width of columns starting from the largest one until the max width is satisfied
            for (int a = 0; a < 15 && maxWidth < widthSum - 0.1; a++) // limit iteration so bug won't create infinite loop
            {
                int nonMaxedColumns = 0;
                double largeWidth = 0f, secLargeWidth = 0f;

                for (int i = 0; i < _columnWidths.Length; i++)
                {
                    if (_columnWidths[i] > largeWidth + 0.1)
                    {
                        secLargeWidth = largeWidth;
                        largeWidth = _columnWidths[i];
                        nonMaxedColumns = 1;
                    }
                    else if (_columnWidths[i] > largeWidth - 0.1)
                    {
                        nonMaxedColumns++;
                    }
                }

                double decrease = secLargeWidth > 0 ? largeWidth - secLargeWidth : (widthSum - maxWidth) / _columnWidths.Length;
                if (decrease * nonMaxedColumns > widthSum - maxWidth)
                    decrease = (widthSum - maxWidth) / nonMaxedColumns;

                for (int i = 0; i < _columnWidths.Length; i++)
                    if (_columnWidths[i] > largeWidth - 0.1)
                        _columnWidths[i] -= decrease;

                widthSum = GetWidthSum();
            }
        }
        else
        {
            // spread extra width to columns that didn't reached max width where trying to spread it between all columns
            for (int a = 0; a < 15 && maxWidth > widthSum + 0.1; a++) // limit iteration so bug won't create infinite loop
            {
                int nonMaxedColumns = 0;
        
                for (int i = 0; i < _columnWidths.Length; i++)
                    if (_columnWidths[i] + 1 < maxFullWidths[i])
                        nonMaxedColumns++;
                
                if (nonMaxedColumns == 0)
                    nonMaxedColumns = _columnWidths.Length;

                bool hit = false;
                double minIncrement = (maxWidth - widthSum) / nonMaxedColumns;
                
                for (int i = 0; i < _columnWidths.Length; i++)
                {
                    if (_columnWidths[i] + 0.1 < maxFullWidths[i])
                    {
                        minIncrement = Math.Min(minIncrement, maxFullWidths[i] - _columnWidths[i]);
                        hit = true;
                    }
                }

                for (int i = 0; i < _columnWidths.Length; i++)
                    if (!hit || _columnWidths[i] + 1 < maxFullWidths[i])
                        _columnWidths[i] += minIncrement;

                widthSum = GetWidthSum();
            }
        }
    }

    /// <summary>
    /// Check for minimum sizes (increment widths if necessary)
    /// </summary>
    private void EnforceMinimumSize()
    {
        foreach (CssBox row in _allRows)
        {
            foreach (CssBox cell in row.Boxes)
            {
                int colspan = GetColSpan(cell);
                int col = GetCellRealColumnIndex(row, cell);
                int affectcol = col + colspan - 1;

                if (_columnWidths.Length <= col || _columnWidths[col] >= GetColumnMinWidths()[col])
                    continue;

                double diff = GetColumnMinWidths()[col] - _columnWidths[col];
                _columnWidths[affectcol] = GetColumnMinWidths()[affectcol];

                if (col < _columnWidths.Length - 1)
                    _columnWidths[col + 1] -= diff;
            }
        }
    }

    private void LayoutCells(ILayoutEnvironment g)
    {
        // CSS2.1 §17.4.1: lay out top-side captions above the cell grid. They
        // span the table's used width and push the first row (and every later
        // row) down by their combined height. Bottom-side captions are laid out
        // after the rows (see below).
        double captionWidth = GetWidthSum() + GetHorizontalSpacing() * (_columnCount + 1);
        double topCaptionHeight = LayoutTopCaptions(g, captionWidth);

        double startx = Math.Max(_tableBox.ClientLeft + GetHorizontalSpacing(), 0);
        double starty = Math.Max(_tableBox.ClientTop + topCaptionHeight + GetVerticalSpacing(), 0);
        double cury = starty;
        double maxRight = startx;
        double maxBottom = 0f;
        int currentrow = 0;

        // CSS2.1 §17.5.3: record each laid-out row's natural top/bottom so a
        // specified table height greater than the content height can be
        // distributed across the rows afterwards.
        var rowBounds = new List<(CssBox Row, double Top, double Bottom)>();

        // Rowspan cells are normally sized/aligned at their last spanned row via a
        // CssSpacingBox. When the trailing spanned rows are collapsed or empty no
        // spacer runs, so we track which spanned cells were finalised and fix up the
        // rest in a post-pass below.
        var finalizedSpanCells = new HashSet<CssBox>();

        for (int i = 0; i < _allRows.Count; i++)
        {
            var row = _allRows[i];
            double rowTop = cury;

            // CSS2.1 §17.5.5: Rows with visibility:collapse are hidden and do
            // not contribute height.  Column widths are still affected (handled
            // during column width calculation).
            if (row.Visibility == CssConstants.Collapse)
            {
                currentrow++;
                continue;
            }

            double curx = startx;
            int curCol = 0;
            bool breakPage = false;

            for (int j = 0; j < row.Boxes.Count; j++)
            {
                CssBox cell = row.Boxes[j];
                if (curCol >= _columnWidths.Length)
                    break;

                int rowspan = GetRowSpan(cell);
                var columnIndex = GetCellRealColumnIndex(row, cell);
                double width = GetCellWidth(columnIndex, cell);

                cell.Location = new PointF((float)curx, (float)cury);
                cell.Size = new SizeF((float)width, 0f);
                cell.PerformLayout(g); //That will automatically set the bottom of the cell

                //Alter max bottom only if row is cell's row + cell's rowspan - 1
                if (cell is CssSpacingBox sb)
                {
                    if (sb.EndRow == currentrow)
                        maxBottom = Math.Max(maxBottom, sb.ExtendedBox.ActualBottom);
                }
                else if (rowspan == 1)
                {
                    maxBottom = Math.Max(maxBottom, cell.ActualBottom);
                }

                maxRight = Math.Max(maxRight, cell.ActualRight);
                curCol++;
                curx = cell.ActualRight + GetHorizontalSpacing();
            }

            // CSS2.1 §17.5.3: a row's specified `height` is a minimum. The loop
            // above only grows `maxBottom` from non-row-spanning cell bottoms, so
            // a row whose only cells span into later rows (e.g. rowspan cells with
            // a collapsed/empty following row) would leave the row — and the whole
            // table — at zero height, which `overflow:hidden` then clips away.
            // Floor the row bottom by its explicit height so such rows still take
            // space and overflowing cell content is clipped to the row box.
            maxBottom = Math.Max(maxBottom, rowTop + GetSpecifiedRowHeight(row));

            foreach (CssBox cell in row.Boxes)
            {
                CssSpacingBox spacer = cell as CssSpacingBox;

                if (spacer == null && GetRowSpan(cell) == 1)
                {
                    cell.ActualBottom = maxBottom;
                    // CSS2.1 §17.5.3: Update Size.Height to match the
                    // stretched cell so background painting uses the full
                    // cell height.
                    cell.Size = new SizeF(cell.Size.Width, (float)(maxBottom - cell.Location.Y));
                    CssLayoutEngine.ApplyCellVerticalAlignment(g, cell);
                }
                else if (spacer != null && spacer.EndRow == currentrow)
                {
                    spacer.ExtendedBox.ActualBottom = maxBottom;
                    spacer.ExtendedBox.Size = new SizeF(spacer.ExtendedBox.Size.Width, (float)(maxBottom - spacer.ExtendedBox.Location.Y));
                    CssLayoutEngine.ApplyCellVerticalAlignment(g, spacer.ExtendedBox);
                    finalizedSpanCells.Add(spacer.ExtendedBox);
                }

                // If one cell crosses page borders then don't need to check other cells in the row
                if (_tableBox.PageBreakInside == CssConstants.Avoid)
                {
                    breakPage = cell.BreakPage();
                    if (breakPage)
                    {
                        cury = cell.Location.Y;
                        break;
                    }
                }
            }

            if (breakPage) // go back to move the whole row to the next page
            {
                if (i == 1) // do not leave single row in previous page
                    i = -1; // Start layout from the first row on new page
                else
                    i--;

                maxBottom = 0;
                continue;
            }

            rowBounds.Add((row, rowTop, maxBottom));
            cury = maxBottom + GetVerticalSpacing();

            currentrow++;
        }

        // CSS2.1 §17.5: finalise rowspan cells that no spacer sized (their trailing
        // spanned rows were collapsed/empty). Clamp each to the bottom of its last
        // laid-out spanned row and align its content — so e.g. `align-content:
        // unsafe end` shifts the overflowing content to the cell's end edge instead
        // of leaving it at the start.
        FinalizeUnspacedRowSpanCells(g, rowBounds, finalizedSpanCells);

        // CSS2.1 §17.5.3: when the table's specified height exceeds the height
        // the rows naturally occupy, distribute the surplus over the rows.
        maxBottom = DistributeExtraTableHeight(g, rowBounds, maxBottom, starty);

        maxRight = Math.Max(maxRight, _tableBox.Location.X + _tableBox.ActualWidth);
        _tableBox.ActualRight = maxRight + GetHorizontalSpacing() + _tableBox.ActualBorderRightWidth;
        _tableBox.ActualBottom = Math.Max(maxBottom, starty) + GetVerticalSpacing() + _tableBox.ActualBorderBottomWidth;

        // CSS2.1 §17.4.1: lay out bottom-side captions below the table box and
        // extend the table's bottom to enclose them.
        double bottomCaptionBottom = LayoutBottomCaptions(g, captionWidth, _tableBox.ActualBottom);
        if (bottomCaptionBottom > _tableBox.ActualBottom)
            _tableBox.ActualBottom = bottomCaptionBottom;

        // CSS2.1 §17.5.2: Update the table box's Size to match the
        // computed layout dimensions so background painting, overflow
        // clipping, and child containing-block queries use the correct
        // table bounds.
        _tableBox.Size = new SizeF(
            (float)(_tableBox.ActualRight - _tableBox.Location.X),
            (float)(_tableBox.ActualBottom - _tableBox.Location.Y));
    }

    /// <summary>
    /// CSS2.1 §17.4.1: A caption's <c>caption-side</c> places it on the block
    /// (top/bottom) side of the table. Broiler exposes it as a raw string
    /// property (default <c>top</c>); only <c>bottom</c> moves it below.
    /// </summary>
    private static bool IsBottomCaption(CssBox caption) =>
        string.Equals(caption.CaptionSide, CssConstants.Bottom, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Lay out a single caption as a block box of the table's used width and
    /// return its outer (margin-box) height. The caption is positioned at
    /// (<paramref name="x"/>, <paramref name="y"/>) at its content-box origin,
    /// inside its own margins.
    /// </summary>
    private static double LayoutCaption(ILayoutEnvironment g, CssBox caption, double x, double y, double width)
    {
        double contentWidth = Math.Max(0,
            width - caption.ActualMarginLeft - caption.ActualMarginRight
                  - caption.ActualBorderLeftWidth - caption.ActualBorderRightWidth
                  - caption.ActualPaddingLeft - caption.ActualPaddingRight);

        // The caption is laid out as a block box whose width is the table's used
        // width (CSS2.1 §17.4). PerformLayoutImp resolves a block's width from its
        // containing block (the table box), whose Size.Width is not finalised
        // until LayoutCells completes — so pin an explicit content-box width here
        // (only when the author left it auto) to avoid the caption shrinking to a
        // stale/zero container width and wrapping every word onto its own line.
        string savedWidth = caption.Width;
        bool pinWidth = string.IsNullOrEmpty(caption.Width) || caption.Width == CssConstants.Auto;

        if (pinWidth)
            caption.Width = contentWidth.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "px";

        double targetX = x + caption.ActualMarginLeft;
        double targetY = y + caption.ActualMarginTop;

        caption.Location = new PointF((float)targetX, (float)targetY);
        caption.Size = new SizeF((float)contentWidth, 0f);
        caption.PerformLayout(g);

        if (pinWidth)
            caption.Width = savedWidth;

        // PerformLayoutImp positions a block's content relative to a flow origin
        // it recomputes, so the caption (and its line boxes) may not honour the
        // provisional Location set above — snap the whole subtree to the target
        // with OffsetLeft/OffsetTop, mirroring the grid item placement path.
        double dx = targetX - caption.Location.X;
        double dy = targetY - caption.Location.Y;

        if (Math.Abs(dx) > 0.01)
            caption.OffsetLeft(dx);

        if (Math.Abs(dy) > 0.01)
            caption.OffsetTop(dy);

        return (caption.ActualBottom - caption.Location.Y)
            + caption.ActualMarginTop + caption.ActualMarginBottom;
    }

    /// <summary>
    /// CSS2.1 §17.4.1: lay out all top-side captions stacked from the table's
    /// content-box top, returning their combined height so the cell grid can be
    /// offset below them.
    /// </summary>
    private double LayoutTopCaptions(ILayoutEnvironment g, double width)
    {
        double total = 0;
        double x = _tableBox.ClientLeft;
        double top = _tableBox.ClientTop;

        foreach (var caption in _captions)
        {
            if (IsBottomCaption(caption))
                continue;

            total += LayoutCaption(g, caption, x, top + total, width);
        }

        return total;
    }

    /// <summary>
    /// CSS2.1 §17.4.1: lay out all bottom-side captions stacked below the table
    /// box, returning the bottom edge of the last caption (or the incoming
    /// <paramref name="tableBottom"/> when there are none).
    /// </summary>
    private double LayoutBottomCaptions(ILayoutEnvironment g, double width, double tableBottom)
    {
        double x = _tableBox.ClientLeft;
        double y = tableBottom;

        foreach (var caption in _captions)
        {
            if (!IsBottomCaption(caption))
                continue;

            y += LayoutCaption(g, caption, x, y, width);
        }

        return y;
    }

    /// <summary>
    /// CSS2.1 §17.5.3: "If the 'table' or 'inline-table' element's height is
    /// specified [and greater than the sum of the row heights], the row heights
    /// are increased so they sum to the specified height." Broiler sizes the
    /// table purely from content, so an explicit table height was ignored
    /// (every CSS2 tables reftest sets <c>height:2in</c>). Distribute the
    /// surplus equally over the in-flow rows: shift each row down by the surplus
    /// already added above it and grow its (non-row-spanning) cells. Returns the
    /// updated <paramref name="naturalBottom"/>. No-op when the height is auto
    /// or the rows already exceed it, so auto-height tables are unchanged.
    /// </summary>
    private double DistributeExtraTableHeight(
        ILayoutEnvironment g, List<(CssBox Row, double Top, double Bottom)> rowBounds,
        double naturalBottom, double rowAreaTop)
    {
        if (rowBounds.Count == 0)
            return naturalBottom;

        double cbHeight = _tableBox.ContainingBlock?.ActualHeight ?? 0;
        double em = _tableBox.GetEmHeight();

        double target = naturalBottom;

        // CSS2.1 §17.5.3: an explicit table 'height' greater than the content
        // grows the rows. Target bottom for the row area = table top + specified
        // content height (ClientTop already includes the top border/padding; the
        // bottom border/spacing is added by the caller).
        if (!string.IsNullOrEmpty(_tableBox.Height) && _tableBox.Height != CssConstants.Auto)
        {
            double specHeight = CssLengthParser.ParseLength(_tableBox.Height, cbHeight, em);
            if (!double.IsNaN(specHeight) && specHeight > 0)
            {
                double specBottom = _tableBox.Location.Y + specHeight
                    - _tableBox.ActualBorderBottomWidth - _tableBox.ActualPaddingBottom - GetVerticalSpacing();

                if (specBottom > target)
                    target = specBottom;
            }
        }

        // CSS2.1 §17.5.3 / §10.7: a 'min-height' greater than the content grows
        // the rows the same way. In the table wrapper model min-height applies to
        // the inner table box (the rows), *not* the caption, so measure it from
        // the row-area top (below any top caption) rather than the table origin —
        // this is what vertically centres a `vertical-align:middle` cell whose
        // table has a tall min-height (WPT table-grid-item-dynamic-002).
        if (!string.IsNullOrEmpty(_tableBox.MinHeight) && _tableBox.MinHeight != "0")
        {
            double minH = CssLengthParser.ParseLength(_tableBox.MinHeight, cbHeight, em);
            if (!double.IsNaN(minH) && minH > 0)
            {
                double minBottom = rowAreaTop + minH
                    - _tableBox.ActualBorderTopWidth - _tableBox.ActualBorderBottomWidth;

                if (minBottom > target)
                    target = minBottom;
            }
        }

        double surplus = target - naturalBottom;
        if (surplus <= 0.5)
            return naturalBottom;

        double perRow = surplus / rowBounds.Count;
        double shift = 0;

        foreach (var (row, top, bottom) in rowBounds)
        {
            foreach (var cell in row.Boxes)
            {
                if (cell is CssSpacingBox || GetRowSpan(cell) != 1)
                {
                    // Spanned cells: just shift; their height is governed by the
                    // last spanned row. Conservative for this increment.
                    cell.Location = new PointF(cell.Location.X, (float)(cell.Location.Y + shift));
                    continue;
                }

                cell.Location = new PointF(cell.Location.X, (float)(cell.Location.Y + shift));

                double newBottom = bottom + shift + perRow;
                cell.ActualBottom = newBottom;
                cell.Size = new SizeF(cell.Size.Width, (float)(newBottom - cell.Location.Y));

                ResolveCellPercentageHeightChildren(g, cell);
                CssLayoutEngine.ApplyCellVerticalAlignment(g, cell);
            }

            shift += perRow;
        }

        return naturalBottom + surplus;
    }

    /// <summary>
    /// CSS2.1 §17.5.3 / §10.5: a table cell's used height is only known after the
    /// row-height pass, so a percentage-height in-flow child laid out during the cell's
    /// own <c>PerformLayout</c> (when the cell was still content-sized) resolved its
    /// height against an indefinite base and fell back to its content height. Once the
    /// cell has been stretched to its final height, make that height definite and re-run
    /// the cell layout so such children resolve against — and fill — the cell (WPT
    /// css-sizing/table-child-percentage-height-with-border-box). The original CSS
    /// <c>height</c> is restored afterwards so a later full relayout pass re-derives it
    /// naturally rather than inheriting this pass's pixel value.
    /// </summary>
    private void ResolveCellPercentageHeightChildren(ILayoutEnvironment g, CssBox cell)
    {
        if (cell is CssSpacingBox || !CellHasPercentageHeightChild(cell))
            return;

        double finalBorderBoxHeight = cell.ActualBottom - cell.Location.Y;
        if (finalBorderBoxHeight <= 0)
            return;

        double edges = cell.ActualBorderTopWidth + cell.ActualBorderBottomWidth
            + cell.ActualPaddingTop + cell.ActualPaddingBottom;
        double cssHeight = string.Equals(cell.BoxSizing, "border-box", StringComparison.OrdinalIgnoreCase)
            ? finalBorderBoxHeight
            : Math.Max(0, finalBorderBoxHeight - edges);

        string savedHeight = cell.Height;
        var savedLocation = cell.Location;
        cell.Height = cssHeight.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "px";
        cell.PerformLayout(g);
        // Keep the finalized cell geometry; only the children needed re-resolution.
        cell.Height = savedHeight;
        cell.Location = savedLocation;
        cell.ActualBottom = savedLocation.Y + finalBorderBoxHeight;
        cell.Size = new SizeF(cell.Size.Width, (float)finalBorderBoxHeight);
    }

    /// <summary>Whether <paramref name="cell"/> has an in-flow child whose <c>height</c>
    /// is a percentage — the case that needs re-resolution once the cell height is final.</summary>
    private static bool CellHasPercentageHeightChild(CssBox cell)
    {
        foreach (var child in cell.Boxes)
        {
            if (child.Position == CssConstants.Absolute || child.Position == CssConstants.Fixed)
                continue;
            if (child.Display == CssConstants.None)
                continue;
            var h = child.Height;
            if (!string.IsNullOrEmpty(h) && h.EndsWith("%", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private double GetSpannedMinWidth(CssBox row, int realcolindex, int colspan)
    {
        double w = 0f;

        for (int i = realcolindex; i < row.Boxes.Count || i < realcolindex + colspan - 1; i++)
        {
            if (i < GetColumnMinWidths().Length)
                w += GetColumnMinWidths()[i];
        }

        return w;
    }

    // CSS2.1 §17.6.2.1 border-conflict-resolution priority of border styles
    // (most → least): hidden (handled separately) > double > solid > dashed >
    // dotted > ridge > outset > groove > inset > none.
    private static readonly Dictionary<string, int> BorderStyleRank = new(StringComparer.OrdinalIgnoreCase)
    {
        ["double"] = 8, ["solid"] = 7, ["dashed"] = 6, ["dotted"] = 5,
        ["ridge"] = 4, ["outset"] = 3, ["groove"] = 2, ["inset"] = 1, ["none"] = 0,
    };

    private readonly record struct EdgeBorder(string Style, double Width, string Color);

    /// <summary>
    /// CSS2.1 §17.6.2.1: resolves the single border that the
    /// <c>border-collapse:collapse</c> model paints at a shared edge between two
    /// cells. <c>hidden</c> suppresses the edge entirely; otherwise the wider
    /// border wins, then the higher-priority style, then (for an exact tie) the
    /// first operand — the spec's earlier-in-tree-order cell. <c>none</c> / zero
    /// width always loses.
    /// </summary>
    private static EdgeBorder ResolveCollapsedEdge(EdgeBorder a, EdgeBorder b)
    {
        bool aHidden = string.Equals(a.Style, CssConstants.Hidden, StringComparison.OrdinalIgnoreCase);
        bool bHidden = string.Equals(b.Style, CssConstants.Hidden, StringComparison.OrdinalIgnoreCase);
        if (aHidden || bHidden)
            return new EdgeBorder(CssConstants.Hidden, 0, string.Empty);

        bool aNone = a.Width <= 0.01 || string.IsNullOrEmpty(a.Style) || string.Equals(a.Style, CssConstants.None, StringComparison.OrdinalIgnoreCase);
        bool bNone = b.Width <= 0.01 || string.IsNullOrEmpty(b.Style) || string.Equals(b.Style, CssConstants.None, StringComparison.OrdinalIgnoreCase);
        if (aNone && bNone)
            return new EdgeBorder(CssConstants.None, 0, string.Empty);

        if (aNone) return b;
        if (bNone) return a;

        if (Math.Abs(a.Width - b.Width) > 0.01)
            return a.Width > b.Width ? a : b;

        int ra = BorderStyleRank.GetValueOrDefault(a.Style, 0);
        int rb = BorderStyleRank.GetValueOrDefault(b.Style, 0);

        if (ra != rb)
            return ra > rb ? a : b;

        return a; // exact tie → the earlier (left/top) cell, per tree order.
    }

    /// <summary>
    /// CSS2.1 §17.6.2.1: in the collapsing-borders model adjacent cells share a
    /// single border. Broiler paints each cell's own borders, so resolve every
    /// internal shared edge to one winner: assign it to the left/top cell and
    /// suppress the right/bottom cell's matching edge, so the edge is painted
    /// once with the winning style/width/colour (and not at all when a
    /// <c>hidden</c> border wins). Perimeter cell edges additionally collapse
    /// with the table element's own border (cell wins ties, per origin
    /// priority). Cells spanning rows/columns (EmptyBox placeholders) are
    /// skipped — a conservative no-op for now.
    /// </summary>
    private void ResolveCollapsedBorders()
    {
        if (!string.Equals(_tableBox.BorderCollapse, CssConstants.Collapse, StringComparison.OrdinalIgnoreCase))
            return;

        if (_allRows.Count == 0)
            return;

        // CSS2.1 §17.6.2.1: an exact tie (same width and style) favours the cell
        // furthest to the top-left in a left-to-right table, but the top-RIGHT
        // cell in a right-to-left table. ResolveCollapsedEdge breaks ties toward
        // its first operand, so for a horizontal edge in an RTL table the right
        // cell must be passed first.
        bool rtl = string.Equals(_tableBox.Direction, "rtl", StringComparison.OrdinalIgnoreCase);

        // Build a column-indexed grid of real cells (a colspan cell occupies
        // every column it spans; row-span placeholders are left null).
        var grid = new List<Dictionary<int, CssBox>>(_allRows.Count);
        foreach (var row in _allRows)
        {
            var cols = new Dictionary<int, CssBox>();
            foreach (var cell in row.Boxes)
            {
                if (cell.Display != CssConstants.TableCell)
                    continue;

                int col = GetCellRealColumnIndex(row, cell);
                int span = GetColSpan(cell);

                for (int k = 0; k < span; k++)
                    cols[col + k] = cell;
            }

            grid.Add(cols);
        }

        // Horizontal internal edges: left cell's right border vs right cell's left.
        foreach (var cols in grid)
        {
            if (cols.Count == 0) continue;

            int maxCol = 0;
            foreach (var c in cols.Keys) if (c > maxCol) maxCol = c;

            for (int c = 0; c < maxCol; c++)
            {
                if (!cols.TryGetValue(c, out var left) || !cols.TryGetValue(c + 1, out var right) || ReferenceEquals(left, right))
                    continue;

                var leftEdge = new EdgeBorder(left.BorderRightStyle, left.ActualBorderRightWidth, left.BorderRightColor);
                var rightEdge = new EdgeBorder(right.BorderLeftStyle, right.ActualBorderLeftWidth, right.BorderLeftColor);
                var winner = rtl
                    ? ResolveCollapsedEdge(rightEdge, leftEdge)
                    : ResolveCollapsedEdge(leftEdge, rightEdge);

                ApplyEdge(left, "right", winner);
                SuppressEdge(right, "left");
            }
        }

        // Vertical internal edges: top cell's bottom border vs bottom cell's top.
        for (int r = 0; r + 1 < grid.Count; r++)
        {
            foreach (var (col, top) in grid[r])
            {
                if (!grid[r + 1].TryGetValue(col, out var bottom) || ReferenceEquals(top, bottom))
                    continue;

                var winner = ResolveCollapsedEdge(
                    new EdgeBorder(top.BorderBottomStyle, top.ActualBorderBottomWidth, top.BorderBottomColor),
                    new EdgeBorder(bottom.BorderTopStyle, bottom.ActualBorderTopWidth, bottom.BorderTopColor));

                ApplyEdge(top, "bottom", winner);
                SuppressEdge(bottom, "top");
            }
        }

        // Outer (perimeter) edges: each border-box cell edge on the table
        // perimeter collapses with the table element's own border. §17.6.2.1
        // origin priority puts the cell above the table, so on an exact tie the
        // cell wins — pass the cell edge first. A no-op for the common
        // borderless table (the table edge is `none`, which always loses).
        var tableTop = new EdgeBorder(_tableBox.BorderTopStyle, _tableBox.ActualBorderTopWidth, _tableBox.BorderTopColor);
        var tableBottom = new EdgeBorder(_tableBox.BorderBottomStyle, _tableBox.ActualBorderBottomWidth, _tableBox.BorderBottomColor);
        var tableLeft = new EdgeBorder(_tableBox.BorderLeftStyle, _tableBox.ActualBorderLeftWidth, _tableBox.BorderLeftColor);
        var tableRight = new EdgeBorder(_tableBox.BorderRightStyle, _tableBox.ActualBorderRightWidth, _tableBox.BorderRightColor);

        int lastRow = grid.Count - 1;
        int globalMaxCol = 0;
        foreach (var cols in grid)
            foreach (var c in cols.Keys)
                if (c > globalMaxCol) globalMaxCol = c;

        var resolvedOuter = new HashSet<(CssBox, string)>();
        for (int r = 0; r < grid.Count; r++)
        {
            foreach (var (col, cell) in grid[r])
            {
                if (r == 0) ResolveOuterEdge(cell, "top", tableTop, resolvedOuter);
                if (r == lastRow) ResolveOuterEdge(cell, "bottom", tableBottom, resolvedOuter);
                if (col == 0) ResolveOuterEdge(cell, "left", tableLeft, resolvedOuter);
                if (col == globalMaxCol) ResolveOuterEdge(cell, "right", tableRight, resolvedOuter);
            }
        }
    }

    private static void ResolveOuterEdge(CssBox cell, string side, EdgeBorder tableEdge, HashSet<(CssBox, string)> done)
    {
        if (!done.Add((cell, side)))
            return;

        EdgeBorder cellEdge = side switch
        {
            "top" => new EdgeBorder(cell.BorderTopStyle, cell.ActualBorderTopWidth, cell.BorderTopColor),
            "bottom" => new EdgeBorder(cell.BorderBottomStyle, cell.ActualBorderBottomWidth, cell.BorderBottomColor),
            "left" => new EdgeBorder(cell.BorderLeftStyle, cell.ActualBorderLeftWidth, cell.BorderLeftColor),
            _ => new EdgeBorder(cell.BorderRightStyle, cell.ActualBorderRightWidth, cell.BorderRightColor),
        };

        // Cell first: §17.6.2.1 origin priority favours the cell over the table
        // on an exact width/style tie.
        ApplyEdge(cell, side, ResolveCollapsedEdge(cellEdge, tableEdge));
    }

    private static void ApplyEdge(CssBox cell, string side, EdgeBorder b)
    {
        // A hidden/none winner paints nothing at this edge.
        bool paints = b.Width > 0.01
            && !string.Equals(b.Style, CssConstants.Hidden, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(b.Style, CssConstants.None, StringComparison.OrdinalIgnoreCase);

        if (!paints)
        {
            SuppressEdge(cell, side);
            return;
        }

        string w = b.Width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";
        switch (side)
        {
            case "right": cell.BorderRightStyle = b.Style; cell.BorderRightWidth = w; cell.BorderRightColor = b.Color; break;
            case "left": cell.BorderLeftStyle = b.Style; cell.BorderLeftWidth = w; cell.BorderLeftColor = b.Color; break;
            case "bottom": cell.BorderBottomStyle = b.Style; cell.BorderBottomWidth = w; cell.BorderBottomColor = b.Color; break;
            case "top": cell.BorderTopStyle = b.Style; cell.BorderTopWidth = w; cell.BorderTopColor = b.Color; break;
        }
    }

    private static void SuppressEdge(CssBox cell, string side)
    {
        switch (side)
        {
            case "right": cell.BorderRightStyle = CssConstants.None; cell.BorderRightWidth = "0"; break;
            case "left": cell.BorderLeftStyle = CssConstants.None; cell.BorderLeftWidth = "0"; break;
            case "bottom": cell.BorderBottomStyle = CssConstants.None; cell.BorderBottomWidth = "0"; break;
            case "top": cell.BorderTopStyle = CssConstants.None; cell.BorderTopWidth = "0"; break;
        }
    }

    private static int GetCellRealColumnIndex(CssBox row, CssBox cell)
    {
        int i = 0;

        foreach (CssBox b in row.Boxes)
        {
            if (b.Equals(cell))
                break;

            i += GetColSpan(b);
        }

        return i;
    }

    private double GetCellWidth(int column, CssBox b)
    {
        double colspan = Convert.ToSingle(GetColSpan(b));
        double sum = 0f;

        for (int i = column; i < column + colspan; i++)
        {
            if (column >= _columnWidths.Length)
                break;

            if (_columnWidths.Length <= i)
                break;

            sum += _columnWidths[i];
        }

        sum += (colspan - 1) * GetHorizontalSpacing();

        return sum; // -b.ActualBorderLeftWidth - b.ActualBorderRightWidth - b.ActualPaddingRight - b.ActualPaddingLeft;
    }

    private static int GetColSpan(CssBox b)
    {
        string att = b.GetAttribute("colspan", "1");

        if (!int.TryParse(att, out int colspan) || colspan < 1)
            return 1;

        return colspan;
    }

    private static int GetRowSpan(CssBox b)
    {
        string att = b.GetAttribute("rowspan", "1");

        if (!int.TryParse(att, out int rowspan))
            return 1;

        return rowspan;
    }

    /// <summary>
    /// CSS2.1 §17.5.3: the explicit <c>height</c> of a table row is a minimum.
    /// Returns the resolved length for a definite (px/em) row height, or 0 when
    /// the height is <c>auto</c> or a percentage (percentages resolve against the
    /// table height, which is not yet known during the row-height pass).
    /// </summary>
    private static double GetSpecifiedRowHeight(CssBox row)
    {
        string h = row.Height;
        if (string.IsNullOrEmpty(h)
            || h == CssConstants.Auto
            || h.EndsWith('%'))
            return 0;

        double v = CssLengthParser.ParseLength(h, 0, row.GetEmHeight());
        return double.IsNaN(v) || v < 0 ? 0 : v;
    }

    /// <summary>
    /// Sizes and aligns rowspan cells that no <see cref="CssSpacingBox"/> finalised
    /// because their trailing spanned rows were collapsed (<c>visibility:collapse</c>)
    /// or empty. Each such cell is clamped to the bottom of its last laid-out spanned
    /// row, then its content is aligned via
    /// <see cref="CssLayoutEngine.ApplyCellContentAlignment"/> (so <c>align-content</c>
    /// center/end positions overflowing content instead of leaving it at the start).
    /// </summary>
    private void FinalizeUnspacedRowSpanCells(
        ILayoutEnvironment g,
        List<(CssBox Row, double Top, double Bottom)> rowBounds,
        HashSet<CssBox> finalizedSpanCells)
    {
        if (rowBounds.Count == 0)
            return;

        var rowBottom = new Dictionary<CssBox, double>();
        foreach (var (row, _, bottom) in rowBounds)
            rowBottom[row] = bottom;

        for (int r = 0; r < _allRows.Count; r++)
        {
            foreach (var cell in _allRows[r].Boxes)
            {
                if (cell is CssSpacingBox || GetRowSpan(cell) <= 1 || finalizedSpanCells.Contains(cell))
                    continue;

                // Bottom of the last laid-out (non-collapsed) row this cell spans.
                double bottom = double.NaN;
                for (int k = r; k < r + GetRowSpan(cell) && k < _allRows.Count; k++)
                    if (rowBottom.TryGetValue(_allRows[k], out var b))
                        bottom = double.IsNaN(bottom) ? b : Math.Max(bottom, b);

                if (double.IsNaN(bottom) || bottom <= cell.Location.Y)
                    continue;

                cell.ActualBottom = bottom;
                cell.Size = new SizeF(cell.Size.Width, (float)(bottom - cell.Location.Y));

                CssLayoutEngine.ApplyCellContentAlignment(g, cell);
                finalizedSpanCells.Add(cell);
            }
        }
    }

    private static void MeasureWords(CssBox box, ILayoutEnvironment g)
    {
        if (box == null)
            return;

        foreach (var childBox in box.Boxes)
        {
            childBox.MeasureWordsSize(g);
            MeasureWords(childBox, g);
        }
    }

    private bool CanReduceWidth()
    {
        for (int i = 0; i < _columnWidths.Length; i++)
        {
            if (CanReduceWidth(i))
                return true;
        }

        return false;
    }

    private bool CanReduceWidth(int columnIndex)
    {
        if (_columnWidths.Length >= columnIndex || GetColumnMinWidths().Length >= columnIndex)
            return false;

        return _columnWidths[columnIndex] > GetColumnMinWidths()[columnIndex];
    }

    private double GetAvailableTableWidth()
    {
        CssLength tblen = new(_tableBox.Width);

        if (tblen.Number > 0)
        {
            _widthSpecified = true;
            return CssLengthParser.ParseLength(_tableBox.Width, _tableBox.ParentBox.AvailableWidth, _tableBox.GetEmHeight());
        }
        else
        {
            return _tableBox.ParentBox.AvailableWidth;
        }
    }

    private double GetMaxTableWidth()
    {
        var tblen = new CssLength(_tableBox.MaxWidth);
        if (tblen.Number > 0)
        {
            _widthSpecified = true;
            return CssLengthParser.ParseLength(_tableBox.MaxWidth, _tableBox.ParentBox.AvailableWidth, _tableBox.GetEmHeight());
        }
        else
        {
            return 9999f;
        }
    }

    private void GetColumnsMinMaxWidthByContent(bool onlyNans, out double[] minFullWidths, out double[] maxFullWidths)
    {
        maxFullWidths = new double[_columnWidths.Length];
        minFullWidths = new double[_columnWidths.Length];

        foreach (CssBox row in _allRows)
        {
            for (int i = 0; i < row.Boxes.Count; i++)
            {
                int col = GetCellRealColumnIndex(row, row.Boxes[i]);
                col = _columnWidths.Length > col ? col : _columnWidths.Length - 1;

                if (onlyNans && !double.IsNaN(_columnWidths[col]) || i >= row.Boxes.Count)
                    continue;

                row.Boxes[i].GetMinMaxWidth(out double minWidth, out double maxWidth);

                var colSpan = GetColSpan(row.Boxes[i]);
                minWidth /= colSpan;
                maxWidth /= colSpan;

                for (int j = 0; j < colSpan; j++)
                {
                    var colIndex = col + j;

                    if (colIndex < minFullWidths.Length)
                        minFullWidths[colIndex] = Math.Max(minFullWidths[colIndex], minWidth);

                    if (colIndex < maxFullWidths.Length)
                        maxFullWidths[colIndex] = Math.Max(maxFullWidths[colIndex], maxWidth);
                }
            }
        }
    }

    private double GetAvailableCellWidth() => GetAvailableTableWidth() - GetHorizontalSpacing() * (_columnCount + 1) - _tableBox.ActualBorderLeftWidth - _tableBox.ActualBorderRightWidth;

    private double GetWidthSum()
    {
        double f = 0f;

        foreach (double t in _columnWidths)
        {
            if (double.IsNaN(t))
                throw new Exception("CssTable Algorithm error: There's a NaN in column widths");
            else
                f += t;
        }

        //Take cell-spacing
        f += GetHorizontalSpacing() * (_columnWidths.Length + 1);

        //Take table borders
        f += _tableBox.ActualBorderLeftWidth + _tableBox.ActualBorderRightWidth;

        return f;
    }

    private static int GetSpan(CssBox b)
    {
        double f = CssLengthParser.ParseNumber(b.GetAttribute("span"), 1);
        return Math.Max(1, Convert.ToInt32(f));
    }

    private double[] GetColumnMinWidths()
    {
        if (_columnMinWidths != null)
            return _columnMinWidths;

        _columnMinWidths = new double[_columnWidths.Length];

        foreach (CssBox row in _allRows)
        {
            foreach (CssBox cell in row.Boxes)
            {
                int colspan = GetColSpan(cell);
                int col = GetCellRealColumnIndex(row, cell);
                int affectcol = Math.Min(col + colspan, _columnMinWidths.Length) - 1;
                double spannedwidth = GetSpannedMinWidth(row, col, colspan) + (colspan - 1) * GetHorizontalSpacing();

                _columnMinWidths[affectcol] = Math.Max(_columnMinWidths[affectcol], cell.GetMinimumWidth() - spannedwidth);
            }
        }

        return _columnMinWidths;
    }

    private double GetHorizontalSpacing() => _tableBox.BorderCollapse == CssConstants.Collapse ? -1f : _tableBox.ActualBorderSpacingHorizontal;
    private static double GetHorizontalSpacing(CssBox box) => box.BorderCollapse == CssConstants.Collapse ? -1f : box.ActualBorderSpacingHorizontal;
    private double GetVerticalSpacing() => _tableBox.BorderCollapse == CssConstants.Collapse ? -1f : _tableBox.ActualBorderSpacingVertical;
}
