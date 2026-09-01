namespace LocalIA.Core.Abstractions;

/// <summary>
/// Suit les connexions actives par palier pour les paliers que le routeur a lui-même chargés —
/// jamais pour un modèle chargé manuellement (Dashboard, Chat/Test) : l'absence d'entrée pour un
/// palier signifie "protégé, jamais candidat à l'éviction automatique du routeur".
/// </summary>
public interface IRouterConnectionTracker
{
    /// <summary>À appeler avant toute action de chargement pour ce palier, et à disposer une fois
    /// la requête terminée (succès ou échec) — l'ordre est important pour éviter une course avec
    /// le balayage d'inactivité.</summary>
    IDisposable BeginLease(Guid tierId);

    int GetActiveCount(Guid tierId);
    DateTimeOffset? GetLastActivityAt(Guid tierId);

    /// <summary>Photo de l'état courant (nombre de connexions actives par palier suivi) — utilisée
    /// par l'arbitre de ressources pour choisir des candidats à l'éviction, et par le Dashboard
    /// pour afficher la charge en cours.</summary>
    IReadOnlyDictionary<Guid, int> Snapshot();

    /// <summary>
    /// Vérifie-et-marque atomiquement qu'un palier peut être déchargé maintenant : il faut une
    /// entrée existante (jamais un palier non suivi — protection de l'usage manuel), zéro
    /// connexion active, aucune éviction déjà en cours pour ce palier, et une inactivité d'au
    /// moins <paramref name="minIdleDuration"/> (zéro pour un besoin immédiat de place côté
    /// arbitre, la vraie période de grâce configurée pour le balayage d'inactivité en arrière-plan).
    /// Si vrai, marque le palier "éviction en cours" pour qu'un appel concurrent ne le décide pas
    /// deux fois — l'appelant DOIT appeler <see cref="EndEviction"/> dans un `finally` une fois le
    /// déchargement effectif (réussi ou non) pour lever ce marquage.
    /// </summary>
    bool TryBeginEviction(Guid tierId, TimeSpan minIdleDuration);

    /// <summary>
    /// Marque un palier "éviction en cours" sans les vérifications de <see cref="TryBeginEviction"/>
    /// (connexions actives, éviction déjà en cours) — pour le seul cas où l'utilisateur a
    /// explicitement choisi, via la boîte de dialogue de conflit, de libérer un palier occupé.
    /// Sans marquage, le balayage d'inactivité ou une éviction automatique concurrente pourrait
    /// agir sur le même palier pendant que ce déchargement forcé est en cours. Ne fait rien si le
    /// palier n'est pas suivi (jamais chargé par le routeur) — l'appelant DOIT quand même appeler
    /// <see cref="EndEviction"/> dans un `finally`.
    /// </summary>
    void ForceBeginEviction(Guid tierId);

    void EndEviction(Guid tierId);
}
