/// <summary>
/// Mémorise l'état du gun entre les scènes via des variables statiques.
/// Les variables statiques survivent aux transitions de scène dans la même session.
/// </summary>
public static class GunPersistence
{
    /// <summary>Le joueur VR tenait le gun quand la scène a changé.</summary>
    public static bool WasHeld { get; set; } = false;

    /// <summary>True = main droite, False = main gauche.</summary>
    public static bool WasRightHand { get; set; } = true;

    public static void Reset()
    {
        WasHeld      = false;
        WasRightHand = true;
    }
}
