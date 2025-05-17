using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using RouteData = Microsoft.AspNetCore.Routing.RouteData;

namespace DysonNetwork.Sphere.Account.Email;

public class RazorViewRenderer(
    IServiceProvider serviceProvider,
    ILoggerFactory loggerFactory,
    ILogger<RazorViewRenderer> logger
)
{
    public async Task<string> RenderComponentToStringAsync<TComponent, TModel>(TModel model) 
        where TComponent : IComponent
    {
        await using var htmlRenderer = new HtmlRenderer(serviceProvider, loggerFactory);
        
        var viewDictionary = new ViewDataDictionary<TModel>(
            new EmptyModelMetadataProvider(),
            new ModelStateDictionary())
        {
            Model = model
        };
        
        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            try 
            {
                var parameterView = ParameterView.FromDictionary(viewDictionary);
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