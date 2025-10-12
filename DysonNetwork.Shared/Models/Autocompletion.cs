using System.ComponentModel.DataAnnotations;

namespace DysonNetwork.Shared.Models;

public class AutocompletionRequest
{
    [Required] public string Content { get; set; } = null!;
}

public class Autocompletion
{
    public string Type { get; set; } = null!;
    public string Keyword { get; set; } = null!;
    public object Data { get; set; } = null!;
}