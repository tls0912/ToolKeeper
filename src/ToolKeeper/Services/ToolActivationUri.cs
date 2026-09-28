namespace ToolKeeper.Services;

/// <summary>Only a catalog ID may cross the activation boundary, never a path or executable command.</summary>
public static class ToolActivationUri
{
    public static string ForProduct(string productId)
    {
        if (!ProductCatalogService.Definitions.Any(product => product.Id == productId))
            throw new ArgumentException("Unknown ToolKeeper module.", nameof(productId));
        return "toolkeeper://run/" + productId;
    }

    public static bool TryParse(string? value, out string productId)
    {
        productId = "";
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value != value.Trim()
            || value.Any(char.IsControl) || value.Contains('%') || value.Contains('\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("toolkeeper", StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("run", StringComparison.OrdinalIgnoreCase)
            || uri.Port != -1 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return false;
        // Compare the entire input as well: System.Uri otherwise normalizes /../ and /./ segments.
        var candidate = uri.AbsolutePath.TrimStart('/');
        if (!ProductCatalogService.Definitions.Any(product => product.Id == candidate)
            || !value.Equals("toolkeeper://run/" + candidate, StringComparison.OrdinalIgnoreCase))
            return false;
        productId = candidate;
        return true;
    }
}
