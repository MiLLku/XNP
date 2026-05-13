using UnityEngine;

public abstract class ResearchUnlockEffect : ScriptableObject
{
    public abstract void Apply();
    public abstract string GetDescription();
}
