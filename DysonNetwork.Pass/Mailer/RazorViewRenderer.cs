using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace DysonNetwork.Pass.Mailer;

public class RazorViewRenderer(
    IServiceProvider serviceProvider,
    ILoggerFactory loggerFactory,
    ILogger<RazorViewRenderer> logger
)
{
    public async Task<string> RenderComponentToStringAsync<TComponent, TModel>(TModel? model) 
        where TComponent : IComponent
    {
        await using var htmlRenderer = new HtmlRenderer(serviceProvider, loggerFactory);

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            try 
            {
                var dictionary = model?.GetType().GetProperties()
                    .ToDictionary(
                        prop => prop.Name,
                        prop => prop.GetValue(model, null)
                    ) ?? new Dictionary<string, object?>();
                var parameterView = ParameterView.FromDictionary(dictionary);
                var output = await htmlRenderer.RenderComponentAsync<TComponent>(parameterView);
                return output.ToHtmlString();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error rendering component {ComponentName}", typeof(TComponent).Name);
                throw;
            }
        });
    }
}