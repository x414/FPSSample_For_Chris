using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class AutomaticRifleUI : AbilityUI
{
    public static bool IsM700Scoped;

    public override void UpdateAbilityUI(EntityManager entityManager, ref GameTime time)
    {
        var charRepAll = entityManager.GetComponentData<CharacterReplicatedData>(abilityOwner);
        var ability = charRepAll.FindAbilityWithComponent(entityManager,typeof(Ability_AutoRifle.PredictedState));
        GameDebug.Assert(ability != Entity.Null,"AbilityController does not own a Ability_AutoRifle ability");
        
        var state = entityManager.GetComponentData<Ability_AutoRifle.PredictedState>(ability);
		var settings = entityManager.GetComponentData<Ability_AutoRifle.Settings>(ability);
		var isAiming = Ability_AutoRifle.IsAiming(entityManager, abilityOwner);
		IsM700Scoped = isAiming;
		if (IsM700Scoped && Input.GetKeyUp(KeyCode.Escape))
		{
			state.aiming = false;
			state.lastAimButton = false;
			entityManager.SetComponentData(ability, state);
			isAiming = false;
		}

        if (isAiming && m_ScopeOverlayRoot == null)
            CreateScopeOverlay();
        if (m_ScopeOverlayRoot != null && m_ScopeOverlayRoot.activeSelf != isAiming)
            m_ScopeOverlayRoot.SetActive(isAiming);
        if (m_ScopeCamera != null && m_ScopeCamera.enabled != isAiming)
            m_ScopeCamera.enabled = isAiming;

        var character = entityManager.GetComponentObject<Character>(abilityOwner);
        if (isAiming)
            UpdateScopeCamera(character.heroTypeData.aimFieldOfView);

        var selectedIcon = character.heroTypeData.hudIcon;
        if (selectedIcon != null && m_WeaponIcon != null && m_WeaponIcon.sprite != selectedIcon)
        {
            m_WeaponIcon.sprite = selectedIcon;
        }
        if (selectedIcon != null && m_WeaponRawImage != null && m_WeaponRawImage.texture != selectedIcon.texture)
        {
            m_WeaponRawImage.texture = selectedIcon.texture;
        }
        
        if (m_AmmoInClip != state.ammoInClip)
        {
            m_AmmoInClip = state.ammoInClip;
            m_AmmoInClipText.text = m_AmmoInClip.ToString();
        }

        if (m_ClipSize != settings.clipSize)
        {
            m_ClipSize = settings.clipSize;
            m_ClipSizeText.text = "/ " + m_ClipSize.ToString();
        }
    }

    [SerializeField] TMPro.TextMeshProUGUI m_AmmoInClipText;
    [SerializeField] TMPro.TextMeshProUGUI m_ClipSizeText;
    [SerializeField] Image m_WeaponIcon;
    
    int m_AmmoInClip = -1;
    int m_ClipSize = -1;
    [SerializeField] RawImage m_WeaponRawImage;

    GameObject m_ScopeOverlayRoot;
    Camera m_ScopeCamera;
    RenderTexture m_ScopeRenderTexture;
    Texture2D m_ScopeMaskTexture;
    Texture2D m_ScopeRingTexture;

    void CreateScopeOverlay()
    {
        var baseCamera = Game.game.TopCamera();
        if (baseCamera == null)
            return;

        var overlayObject = new GameObject("M700ScopeOverlay", typeof(Canvas), typeof(CanvasScaler), typeof(CanvasRenderer));
        overlayObject.transform.SetParent(null, false);
        var overlayCanvas = overlayObject.GetComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 32000;

        var canvasScaler = overlayObject.GetComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = new Vector2(1280f, 720f);
        canvasScaler.matchWidthOrHeight = 0.5f;

        var overlayRect = overlayObject.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlayRect.localScale = Vector3.one;

        m_ScopeOverlayRoot = overlayObject;

        var scopeAreaObject = new GameObject("M700ScopeArea", typeof(RectTransform));
        scopeAreaObject.transform.SetParent(overlayRect, false);
        var scopeAreaRect = scopeAreaObject.GetComponent<RectTransform>();
        scopeAreaRect.anchorMin = Vector2.one * 0.5f;
        scopeAreaRect.anchorMax = Vector2.one * 0.5f;
        scopeAreaRect.offsetMin = Vector2.zero;
        scopeAreaRect.offsetMax = Vector2.zero;
        scopeAreaRect.localScale = Vector3.one;
        scopeAreaRect.sizeDelta = Vector2.one * 620f;
        scopeAreaRect.anchoredPosition = Vector2.zero;
        scopeAreaRect.pivot = new Vector2(0.5f, 0.5f);

        m_ScopeMaskTexture = CreateCircleTexture(512, 0.98f);
        var maskObject = new GameObject("M700ScopeMask", typeof(CanvasRenderer), typeof(Image), typeof(Mask));
        maskObject.transform.SetParent(scopeAreaRect, false);
        var maskRect = maskObject.GetComponent<RectTransform>();
        maskRect.anchorMin = Vector2.zero;
        maskRect.anchorMax = Vector2.one;
        maskRect.offsetMin = Vector2.zero;
        maskRect.offsetMax = Vector2.zero;
        var maskImage = maskObject.GetComponent<Image>();
        maskImage.sprite = Sprite.Create(m_ScopeMaskTexture, new Rect(0f, 0f, m_ScopeMaskTexture.width, m_ScopeMaskTexture.height), Vector2.one * 0.5f, 512f);
        maskImage.raycastTarget = false;
        var mask = maskObject.GetComponent<Mask>();
        mask.showMaskGraphic = false;

        m_ScopeRenderTexture = new RenderTexture(1024, 1024, 24, RenderTextureFormat.ARGB32);
        m_ScopeRenderTexture.antiAliasing = 4;

        m_ScopeCamera = CreateScopeCamera(baseCamera);
        m_ScopeCamera.targetTexture = m_ScopeRenderTexture;

        var scopeViewObject = new GameObject("M700ScopeView", typeof(CanvasRenderer), typeof(RawImage));
        scopeViewObject.transform.SetParent(maskRect, false);
        var scopeViewRect = scopeViewObject.GetComponent<RectTransform>();
        scopeViewRect.anchorMin = Vector2.zero;
        scopeViewRect.anchorMax = Vector2.one;
        scopeViewRect.offsetMin = Vector2.zero;
        scopeViewRect.offsetMax = Vector2.zero;
        var scopeView = scopeViewObject.GetComponent<RawImage>();
        scopeView.texture = m_ScopeRenderTexture;
        scopeView.raycastTarget = false;

        m_ScopeRingTexture = CreateRingTexture(512, 0.90f, 1.00f);
        var ringObject = new GameObject("M700ScopeRing", typeof(CanvasRenderer), typeof(Image));
        ringObject.transform.SetParent(scopeAreaRect, false);
        var ringRect = ringObject.GetComponent<RectTransform>();
        ringRect.anchorMin = Vector2.zero;
        ringRect.anchorMax = Vector2.one;
        ringRect.offsetMin = Vector2.zero;
        ringRect.offsetMax = Vector2.zero;
        var ringImage = ringObject.GetComponent<Image>();
        ringImage.sprite = Sprite.Create(m_ScopeRingTexture, new Rect(0f, 0f, m_ScopeRingTexture.width, m_ScopeRingTexture.height), Vector2.one * 0.5f, 512f);
        ringImage.color = Color.black;
        ringImage.raycastTarget = false;

        CreateCrosshair(scopeAreaRect);
    }

    void CreateCrosshair(RectTransform parent)
    {
        var crosshairObject = new GameObject("M700ScopeCrosshair", typeof(RectTransform));
        crosshairObject.transform.SetParent(parent, false);
        var crosshairRect = crosshairObject.GetComponent<RectTransform>();
        crosshairRect.anchorMin = Vector2.one * 0.5f;
        crosshairRect.anchorMax = Vector2.one * 0.5f;
        crosshairRect.offsetMin = Vector2.zero;
        crosshairRect.offsetMax = Vector2.zero;
        crosshairRect.anchoredPosition = Vector2.zero;
        crosshairRect.pivot = new Vector2(0.5f, 0.5f);
        crosshairRect.sizeDelta = new Vector2(350f, 350f);

        CreateCrosshairLine(crosshairRect, new Vector2(350f, 2f));
        CreateCrosshairLine(crosshairRect, new Vector2(2f, 350f));
    }

    void CreateCrosshairLine(RectTransform parent, Vector2 size)
    {
        var lineObject = new GameObject("M700ScopeCrosshairLine", typeof(RectTransform), typeof(Image));
        lineObject.transform.SetParent(parent, false);
        var lineRect = lineObject.GetComponent<RectTransform>();
        lineRect.anchorMin = Vector2.one * 0.5f;
        lineRect.anchorMax = Vector2.one * 0.5f;
        lineRect.offsetMin = Vector2.zero;
        lineRect.offsetMax = Vector2.zero;
        lineRect.anchoredPosition = Vector2.zero;
        lineRect.pivot = new Vector2(0.5f, 0.5f);
        lineRect.sizeDelta = size;

        var lineImage = lineObject.GetComponent<Image>();
        lineImage.color = Color.black;
        lineImage.raycastTarget = false;
    }

    Camera CreateScopeCamera(Camera baseCamera)
    {
        var scopeObject = Instantiate(baseCamera.gameObject);
        scopeObject.name = "M700ScopeCamera";
        scopeObject.tag = "Untagged";
        scopeObject.transform.SetParent(null, true);
        Destroy(scopeObject.GetComponent<AudioListener>());
        Destroy(scopeObject.GetComponent<PlayerCamera>());

        var scopeCamera = scopeObject.GetComponent<Camera>();
        scopeCamera.targetTexture = m_ScopeRenderTexture;
        scopeCamera.enabled = true;
        return scopeCamera;
    }

    void UpdateScopeCamera(float aimFieldOfView)
    {
        if (m_ScopeCamera == null || Game.game == null)
            return;

        var baseCamera = Game.game.TopCamera();
        if (baseCamera == null)
            return;

        m_ScopeCamera.transform.position = baseCamera.transform.position;
        m_ScopeCamera.transform.rotation = baseCamera.transform.rotation;
        m_ScopeCamera.fieldOfView = aimFieldOfView;
    }

    void OnDestroy()
    {
        IsM700Scoped = false;
        if (m_ScopeOverlayRoot != null)
            Destroy(m_ScopeOverlayRoot);
        if (m_ScopeCamera != null)
            Destroy(m_ScopeCamera.gameObject);
        if (m_ScopeRenderTexture != null)
        {
            m_ScopeRenderTexture.Release();
            Destroy(m_ScopeRenderTexture);
        }
        if (m_ScopeMaskTexture != null)
            Destroy(m_ScopeMaskTexture);
        if (m_ScopeRingTexture != null)
            Destroy(m_ScopeRingTexture);
    }

    Texture2D CreateCircleTexture(int size, float radius)
    {
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
        var pixels = new Color32[size * size];
        var center = (size - 1) * 0.5f;
        var radiusPixels = radius * center;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var inside = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) <= radiusPixels;
                pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
        }

        texture.SetPixels32(pixels);
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply(false, true);
        return texture;
    }

    Texture2D CreateRingTexture(int size, float innerRadius, float outerRadius)
    {
        var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
        var pixels = new Color32[size * size];
        var center = (size - 1) * 0.5f;
        var innerRadiusPixels = innerRadius * center;
        var outerRadiusPixels = outerRadius * center;

        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                var inside = distance >= innerRadiusPixels && distance <= outerRadiusPixels;
                pixels[y * size + x] = inside ? new Color32(0, 0, 0, 255) : new Color32(0, 0, 0, 0);
            }
        }

        texture.SetPixels32(pixels);
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply(false, true);
        return texture;
    }
}
