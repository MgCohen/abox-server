using System.Reflection;
using ABox.Features.Projects.List;

namespace ABox.Features.Projects.Module;

public static class ProjectsModule
{
    public static Assembly EndpointsAssembly => typeof(ListProjectsEndpoint).Assembly;
}
