using Emitted;
using Probe;
using Probe.Domain;

// Drive BOTH emitted handlers against their real providers. Same feature (add points to
// a user, load → mutate → save), same output (the mutated User), two different stores.

var command = new AddPointsCommand(Email: "ada@example.com", Region: "eu-west", Points: 5);

// --- Repo provider: keyed by Email ---
var repo = new Repo<User>().Seed("ada@example.com", new User("ada"));
var viaRepo = AddPointsRepoHandler.Handle(repo, command);
Console.WriteLine($"Repo   -> user {viaRepo.Id} now has {viaRepo.Points} points");
var reloadedRepo = repo.Load("ada@example.com");
Console.WriteLine($"          persisted? reload shows {reloadedRepo.Points} points");

// --- Bucket provider: keyed by Region (a BucketKey) ---
var bucket = new Bucket<User>().Seed(new BucketKey("eu-west"), new User("ada"));
var viaBucket = AddPointsBucketHandler.Handle(bucket, command);
Console.WriteLine($"Bucket -> user {viaBucket.Id} now has {viaBucket.Points} points");
var reloadedBucket = bucket.Download(new BucketKey("eu-west"));
Console.WriteLine($"          persisted? reload shows {reloadedBucket.Points} points");

var ok = viaRepo.Points == 5 && reloadedRepo.Points == 5
      && viaBucket.Points == 5 && reloadedBucket.Points == 5;
Console.WriteLine(ok
    ? "\nBOTH providers: loaded, mutated, saved — same feature, one recipe shape. OK"
    : "\nFAIL: a provider did not load/mutate/save as expected");
return ok ? 0 : 1;
