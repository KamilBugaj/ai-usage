using System;
using Avalonia.Threading;
using AiUsage.Application.Abstractions;

namespace AiUsage.App.Infrastructure;

/// <summary>Avalonia-backed <see cref="IUiDispatcher"/> — posts onto the UI thread.</summary>
public sealed class AvaloniaUiDispatcher : IUiDispatcher
{
    public void Post(Action action) => Dispatcher.UIThread.Post(action);
}
