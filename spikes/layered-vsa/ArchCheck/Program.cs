using System.Reflection;

Assembly flows = typeof(Flows.FlowsFeature).Assembly;
Assembly notifications = typeof(Notifications.NotificationsFeature).Assembly;

bool ok = true;
ok &= AssertNoRef(flows, "Notifications.Feature");
ok &= AssertNoRef(notifications, "Flows.Feature");

Console.WriteLine();
Console.WriteLine(ok
    ? "ARCHTEST PASS — no sideways Feature -> Feature references."
    : "ARCHTEST FAIL — a slice reached sideways.");
return ok ? 0 : 1;

static bool AssertNoRef(Assembly feature, string forbidden)
{
    bool violates = feature.GetReferencedAssemblies().Any(a => a.Name == forbidden);
    Console.WriteLine($"  {feature.GetName().Name}  -/->  {forbidden}   : {(violates ? "VIOLATION" : "ok")}");
    return !violates;
}
