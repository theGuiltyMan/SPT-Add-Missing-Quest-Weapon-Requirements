using AddMissingQuestRequirements.Util;

namespace AddMissingQuestRequirements.Inspector.Serve;

/// <summary>
/// Loads config, runs the pipeline, and swaps <see cref="StateHolder"/> state.
/// </summary>
public static class ReloadPipeline
{
    /// <summary>
    /// Load config, run pipeline, and swap the holder's state on success.
    /// On any exception, swallow it, record the message on the holder, and
    /// return <c>false</c>. The holder's <c>Result</c> is unchanged on failure.
    /// </summary>
    public static bool Run(InspectorConfig config, StateHolder holder, IModLogger logger)
    {
        try
        {
            var loaded = InspectorLoader.Load(config, logger);
            var result = PipelineRunner.Run(loaded, logger);
            holder.ApplyReload(loaded, result);
            return true;
        }
        catch (Exception ex)
        {
            logger.Warning($"[Inspector] Reload failed: {ex.Message}");
            holder.ApplyReloadError(ex.Message);
            return false;
        }
    }
}
