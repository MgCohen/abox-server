using Emitted;
using Probe;
using Probe.Domain;

// Drive BOTH emitted handlers against their real STATIC stores. Same feature (add points to
// a user, load -> mutate -> save), same output (the mutated User), two different stores. The
// handlers take only the command; the store is the static singleton they reference.

var command = new AddPointsCommand(Email: "ada@example.com", Region: "eu-west", Points: 5);

// --- Repository store: keyed by Email. Seed the static store, then run the handler. ---
Stores.Repository<User>().Save("ada@example.com", new User("ada"));
var viaRepo = AddPointsRepositoryHandler.Handle(command);
Console.WriteLine($"Repository -> user {viaRepo.Id} now has {viaRepo.Points} points");
var reloadedRepo = Stores.Repository<User>().Get("ada@example.com");
Console.WriteLine($"              persisted? reload shows {reloadedRepo.Points} points");

// --- BucketStore: keyed by Region (a BucketKey). Seed the static store, then run. ---
Stores.BucketStore<User>().Save(new BucketKey("eu-west"), new User("ada"));
var viaBucket = AddPointsBucketStoreHandler.Handle(command);
Console.WriteLine($"BucketStore -> user {viaBucket.Id} now has {viaBucket.Points} points");
var reloadedBucket = Stores.BucketStore<User>().Get(new BucketKey("eu-west"));
Console.WriteLine($"              persisted? reload shows {reloadedBucket.Points} points");

var ok = viaRepo.Points == 5 && reloadedRepo.Points == 5
      && viaBucket.Points == 5 && reloadedBucket.Points == 5;
Console.WriteLine(ok
    ? "\nBOTH stores: seeded, loaded, mutated, saved — same standard Mutate, one recipe shape. OK"
    : "\nFAIL: a store did not load/mutate/save as expected");
return ok ? 0 : 1;
