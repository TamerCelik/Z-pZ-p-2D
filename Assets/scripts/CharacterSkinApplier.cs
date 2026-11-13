using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Seçili karakter kostümünü (CharacterShopData) oyun sahnesindeki oyuncunun
/// SpriteRenderer ve Animator bileşenlerine başlangıçta uygular.
/// Fizik, çarpışma ve zıplama efektlerine dokunmaz.
/// </summary>
[DefaultExecutionOrder(-10)]
public class CharacterSkinApplier : MonoBehaviour
{
    private RuntimeAnimatorController baseController;

    private void Awake()
    {
        ApplySelectedSkin();
    }

    public void ApplySelectedSkin()
    {
        CharacterShopConfig config = CharacterShopConfig.Load();
        if (config == null) return;

        string selectedId = CharacterShopData.GetSelectedCharacterId();
        CharacterShopConfig.CharacterVisualEntry entry = config.GetVisualEntry(selectedId);
        if (entry == null) return;

        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null && entry.idleFrames != null && entry.idleFrames.Length > 0)
        {
            sr.sprite = entry.idleFrames[0];
        }

        Animator anim = GetComponent<Animator>();
        if (anim != null && entry.idleClip != null)
        {
            if (baseController == null && anim.runtimeAnimatorController != null)
            {
                baseController = anim.runtimeAnimatorController is AnimatorOverrideController existing
                    ? existing.runtimeAnimatorController
                    : anim.runtimeAnimatorController;
            }

            if (baseController != null)
            {
                AnimatorOverrideController overrideCtrl = new AnimatorOverrideController(baseController);
                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(overrideCtrl.overridesCount);
                overrideCtrl.GetOverrides(overrides);

                for (int i = 0; i < overrides.Count; i++)
                {
                    overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(overrides[i].Key, entry.idleClip);
                }

                overrideCtrl.ApplyOverrides(overrides);
                anim.runtimeAnimatorController = overrideCtrl;
                anim.Rebind();
                anim.Update(0f);
            }
        }
    }
}
