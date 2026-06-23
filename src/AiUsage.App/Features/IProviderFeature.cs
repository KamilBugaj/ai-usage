using AiUsage.App.ViewModels;
using AiUsage.Application.Polling;
using AiUsage.Core.Config;

namespace AiUsage.App.Features;

internal interface IProviderFeature
{
    TileViewModelBase Wire(AppConfig config, IPollScheduler scheduler);
}
