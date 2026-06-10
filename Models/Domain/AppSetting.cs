using System.ComponentModel.DataAnnotations;

namespace SaleOrd.Models.Domain;

public class AppSetting
{
    [Key, MaxLength(100)]
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
