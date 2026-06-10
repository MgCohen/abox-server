using Microsoft.AspNetCore.Components;

namespace Morph;

[EventHandler("onmorphend", typeof(MorphEndEventArgs), enableStopPropagation: false, enablePreventDefault: false)]
public static class EventHandlers
{
}
