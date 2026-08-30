namespace LocalIA.Core.Models;

public enum EngineStatus
{
    Unknown,
    Stopped,
    Starting,
    Running,
    Stopping,
    Crashed,

    /// <summary>L'exécutable n'a pas pu être trouvé du tout (jamais installé, ou pas dans le PATH
    /// ni à l'emplacement d'installation par défaut) — distinct de Crashed, qui suppose qu'un
    /// démarrage a été tenté avec un exécutable trouvé. Sert à afficher un bouton d'installation
    /// plutôt qu'un simple message d'erreur.</summary>
    NotInstalled,
}
