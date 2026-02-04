namespace DysonNetwork.Shared.Templating;

public interface ITemplateService
{
    Task<string> RenderAsync(string templateName, object model, string? locale = null);
    Task<string> RenderWithLayoutAsync(string templateName, string layoutName, object model, string? locale = null);
}
