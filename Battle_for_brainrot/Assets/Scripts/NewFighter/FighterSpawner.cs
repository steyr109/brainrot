using UnityEngine;

public class FighterSpawner : MonoBehaviour
{
    public Transform player1Spawn;

    public GameObject defaultPrefab;

    private void Start()
    {
        if (CharacterSelection.SelectedCharacter != null &&
            CharacterSelection.SelectedCharacter.characterPrefab != null)
        {
            Instantiate(
                CharacterSelection.SelectedCharacter.characterPrefab,
                player1Spawn.position,
                Quaternion.identity
            );

            return;
        }

        if (defaultPrefab != null)
        {
            Instantiate(defaultPrefab, player1Spawn.position, Quaternion.identity);
        }
    }
}
