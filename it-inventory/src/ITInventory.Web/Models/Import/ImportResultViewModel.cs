namespace ITInventory.Web.Models.Import;

public class ImportResultViewModel
{
    public string EntityName { get; set; } = string.Empty;
    public string BackToListAction { get; set; } = "Index";
    public string ImportAgainAction { get; set; } = "Import";
    public int SuccessCount { get; set; }
    public List<ImportRowError> Errors { get; set; } = new();
}

public class ImportRowError
{
    public int RowNumber { get; set; }
    public string Message { get; set; } = string.Empty;
}
