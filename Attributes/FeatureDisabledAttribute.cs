using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Email.Server.Attributes;

/// <summary>
/// Attribute to disable a controller or action with a "Coming Soon" response.
/// Used to temporarily disable features while building reputation.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class FeatureDisabledAttribute : ActionFilterAttribute
{
    private readonly string _featureName;

    public FeatureDisabledAttribute(string featureName = "This feature")
    {
        _featureName = featureName;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        context.Result = new ObjectResult(new
        {
            error = "feature_disabled",
            message = $"{_featureName} is coming soon. We're currently in early access for SMS only.",
            status = 503
        })
        {
            StatusCode = 503
        };
    }
}
