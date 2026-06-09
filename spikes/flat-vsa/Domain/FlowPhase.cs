namespace App.Domain;

public enum PhaseState { Pending, Running, Done, Blocked }

public sealed record FlowPhase(string Name, PhaseState State);
