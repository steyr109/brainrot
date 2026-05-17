using UnityEngine;

[CreateAssetMenu(menuName = "Brainrot/Character Data")]
public class CharacterData : ScriptableObject
{
    [Header("Info")]
    public string characterName;

    [Header("Visuals")]
    public RuntimeAnimatorController animatorController;
    public GameObject characterPrefab;

    [Header("Stats")]
    public int maxHealth = 100;
    public float forwardWalkSpeed = 5f;
    public float backwardWalkSpeed = 5f;
    public float horizontalJumpSpeed = 8f;
    public float verticalJumpSpeed = 3f;
    public float gravity = 50f;
}
