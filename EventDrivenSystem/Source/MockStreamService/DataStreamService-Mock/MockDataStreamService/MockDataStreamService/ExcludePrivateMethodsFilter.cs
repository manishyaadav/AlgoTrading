using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

public class ExcludePrivateMethodsFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        // Exclude private methods from Swagger documentation
        var keysToRemove = context.ApiDescriptions
            .Where(desc => desc.ActionDescriptor.EndpointMetadata
                .OfType<ApiExplorerSettingsAttribute>()
                .Any(attr => attr.IgnoreApi))
            .Select(desc => desc.RelativePath)
            .ToList();

        foreach (var key in keysToRemove)
        {
            swaggerDoc.Paths.Remove(key);
        }
    }
}