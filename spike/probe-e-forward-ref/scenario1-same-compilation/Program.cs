using Scenario1;

// Scenario 1 driver: exercise the minted `Foo` through the use-site in 00_SiteY_Uses.cs.
// `Foo` was minted by the recipe site in ZZ_SiteX_Mints.cs — a SEPARATE file that the
// generator scans compilation-wide. No TypeRef anywhere; `Foo` is a real type.
var holder = new FooHolder();
var total = holder.Add(new Foo(Guid.NewGuid(), "alpha"));
total = holder.Add(new Foo(Guid.NewGuid(), "beta"));

Console.WriteLine($"Current      -> {holder.Current}");
Console.WriteLine($"Repo+List ct -> {total}");
Console.WriteLine("Scenario 1: same-compilation, cross-file, use-before-mint => COMPILED & RAN");
