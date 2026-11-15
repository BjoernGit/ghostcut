using Ghostcut.Core.Model;

namespace Ghostcut.Core.Commands;

/// <summary>
/// Executes commands on a project and keeps the undo/redo stacks.
/// <para>
/// Drags use gestures: <see cref="ExecuteGesture"/> with the same token replaces the previous
/// command of that gesture, so a whole drag is one undo step while the model updates live.
/// </para>
/// </summary>
public sealed class History
{
    private readonly List<IEditCommand> _undo = new();
    private readonly List<IEditCommand> _redo = new();
    private object? _gestureToken;
    private IEditCommand? _savedTop;
    private bool _savedTopValid = true;

    public History(Project project)
    {
        Project = project;
    }

    public Project Project { get; }

    /// <summary>Raised after every change of the project through this history.</summary>
    public event Action? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;

    public string? RedoDescription => _redo.Count > 0 ? _redo[^1].Description : null;

    /// <summary>Upper bound of kept undo steps.</summary>
    public int Capacity { get; set; } = 500;

    /// <summary>True when the project differs from the last <see cref="MarkSaved"/>.</summary>
    public bool IsDirty => !_savedTopValid || !ReferenceEquals(Top, _savedTop);

    private IEditCommand? Top => _undo.Count > 0 ? _undo[^1] : null;

    public void Execute(IEditCommand command)
    {
        EndGesture();
        command.Execute(Project);
        Push(command);
        Changed?.Invoke();
    }

    /// <summary>
    /// Executes a command as part of a gesture. If the previous command belongs to the same gesture,
    /// it is undone first and replaced. If the new command fails, the previous one is restored.
    /// </summary>
    public void ExecuteGesture(IEditCommand command, object gestureToken)
    {
        if (_gestureToken != null && ReferenceEquals(_gestureToken, gestureToken) && _undo.Count > 0)
        {
            var previous = _undo[^1];
            previous.Undo(Project);
            try
            {
                command.Execute(Project);
            }
            catch
            {
                previous.Execute(Project);
                throw;
            }
            _undo[^1] = command;
            if (ReferenceEquals(_savedTop, previous))
                _savedTopValid = false;
        }
        else
        {
            EndGesture();
            command.Execute(Project);
            Push(command);
            _gestureToken = gestureToken;
        }
        Changed?.Invoke();
    }

    /// <summary>Ends the running gesture; the next gesture command starts a new undo step.</summary>
    public void EndGesture() => _gestureToken = null;

    /// <summary>Undoes the running gesture completely (e.g. Escape during a drag).</summary>
    public void CancelGesture(object gestureToken)
    {
        if (_gestureToken != null && ReferenceEquals(_gestureToken, gestureToken) && _undo.Count > 0)
        {
            var command = _undo[^1];
            _undo.RemoveAt(_undo.Count - 1);
            command.Undo(Project);
            Changed?.Invoke();
        }
        _gestureToken = null;
    }

    public bool Undo()
    {
        EndGesture();
        if (_undo.Count == 0)
            return false;
        var command = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        command.Undo(Project);
        _redo.Add(command);
        Changed?.Invoke();
        return true;
    }

    public bool Redo()
    {
        EndGesture();
        if (_redo.Count == 0)
            return false;
        var command = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        command.Execute(Project);
        _undo.Add(command);
        Changed?.Invoke();
        return true;
    }

    public void MarkSaved()
    {
        _savedTop = Top;
        _savedTopValid = true;
    }

    /// <summary>Marks the project as differing from any file (e.g. after recovering an autosave).</summary>
    public void MarkUnsaved() => _savedTopValid = false;

    private void Push(IEditCommand command)
    {
        _undo.Add(command);
        _redo.Clear();
        if (_undo.Count > Capacity)
        {
            if (ReferenceEquals(_undo[0], _savedTop))
                _savedTopValid = false;
            _undo.RemoveAt(0);
        }
    }
}
