using StandAlone.Integration.Services;

namespace StandAlone.CartonUi.Services;

public static class SatoTemplateResolver
{
    public static string? ResolveTemplateFileName(ThermalLabelPayload payload)
        => StandAlone.Integration.Services.SatoTemplateResolver.ResolveTemplateFileName(payload);

    public static string FindTemplateDirectory()
        => StandAlone.Integration.Services.SatoTemplateResolver.FindTemplateDirectory();
}

public static class SatoTemplateRenderer
{
    public static string RenderTemplate(string template, ThermalLabelPayload payload)
        => StandAlone.Integration.Services.SatoTemplateRenderer.RenderTemplate(template, payload);
}
