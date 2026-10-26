namespace AutoTestRunner.Scripting;

/// <summary>A grid's cell: in the row any of whose cells says Row, under the column headed Column.</summary>
public sealed record GridCell(string Row, string Column);
