using Ghostcut.Core.Model;

namespace Ghostcut.Core.Commands;

/// <summary>
/// One edit of the project. Every change — from the UI or later from an agent — is a command, which
/// gives undo/redo for free. <see cref="Execute"/> must validate before it mutates: when it throws,
/// the project is unchanged. <see cref="Undo"/> is only called right after a successful Execute
/// (or Redo) of the same instance and restores the exact previous state.
/// </summary>
public interface IEditCommand
{
    string Description { get; }

    void Execute(Project project);

    void Undo(Project project);
}

/// <summary>Thrown when a command cannot be applied to the current project.</summary>
public sealed class EditException(string message) : InvalidOperationException(message);

/// <summary>Several commands as one undo step, e.g. "import": add source + add clip.</summary>
public sealed class CompositeCommand(string description, IReadOnlyList<IEditCommand> commands) : IEditCommand
{
    public string Description { get; } = description;

    public IReadOnlyList<IEditCommand> Commands { get; } = commands;

    public void Execute(Project project)
    {
        var done = 0;
        try
        {
            for (; done < Commands.Count; done++)
                Commands[done].Execute(project);
        }
        catch
        {
            for (var i = done - 1; i >= 0; i--)
                Commands[i].Undo(project);
            throw;
        }
    }

    public void Undo(Project project)
    {
        for (var i = Commands.Count - 1; i >= 0; i--)
            Commands[i].Undo(project);
    }
}
