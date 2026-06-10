namespace MorphDemo;

public sealed class DemoTransitionState
{
    private string _current = "morph";

    public string Current
    {
        get => _current;
        set
        {
            if (_current == value)
                return;

            _current = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;
}
