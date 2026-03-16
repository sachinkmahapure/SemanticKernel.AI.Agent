using AI.ChatAgent.Models;
using AI.ChatAgent.Plugins;
using Microsoft.SemanticKernel;

namespace AI.ChatAgent.Services;

/// <summary>Abstraction over kernel creation — enables mocking in unit tests.</summary>
public interface IKernelFactory
{
    /// <summary>Returns a per-request <see cref="Kernel"/> with all plugins attached.</summary>
    Kernel CreateForRequest();
}

/// <summary>
/// Creates a per-request <see cref="Kernel"/> with all plugins attached.
///
/// WHY: <c>kernelBuilder.Plugins.AddFromType&lt;T&gt;()</c> registers plugin types
/// as singletons inside SK's internal DI container. Any plugin that depends on a
/// scoped service (e.g. <c>ChatAgentDbContext</c>) will throw
/// "Cannot resolve scoped service from root provider" at runtime.
///
/// SOLUTION: Register the root Kernel (with AI services only, no plugins) as a
/// singleton. Then, per-request, clone the Kernel and attach plugin instances that
/// are resolved from the current HTTP request's DI scope — where scoped services
/// like DbContext are valid.
/// </summary>
public class KernelFactory(
    Kernel rootKernel,
    DatabasePlugin  databasePlugin,
    ApiPlugin       apiPlugin,
    PdfPlugin       pdfPlugin,
    FilePlugin      filePlugin,
    WebSearchPlugin webSearchPlugin) : IKernelFactory
{
    /// <summary>
    /// Build a <see cref="Kernel"/> instance for the current request.
    /// The kernel shares AI completion services from the root kernel but
    /// gets a fresh plugin collection populated from scoped DI instances.
    /// </summary>
    public virtual Kernel CreateForRequest()
    {
        // Clone shares the underlying AI service provider (singleton) but
        // gets its own plugin collection — safe to mutate per request.
        var kernel = rootKernel.Clone();

        kernel.Plugins.AddFromObject(databasePlugin,  AppConstants.Plugins.Database);
        kernel.Plugins.AddFromObject(apiPlugin,       AppConstants.Plugins.Api);
        kernel.Plugins.AddFromObject(pdfPlugin,       AppConstants.Plugins.Pdf);
        kernel.Plugins.AddFromObject(filePlugin,      AppConstants.Plugins.File);
        kernel.Plugins.AddFromObject(webSearchPlugin, AppConstants.Plugins.WebSearch);

        return kernel;
    }
}
