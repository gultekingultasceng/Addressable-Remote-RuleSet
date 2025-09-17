
# Addressables Prefab & Serialization Uyarıları (Örneklerle)

Bu rehber, uzaktan yüklenen Addressables prefab'larının hangi durumlarda bozulabileceğini ve güvenli değişikliklerin nasıl yapılacağını açıklar. Addressables kullanan script ve UI prefab'larını düzenlerken referans olsun.


## Hızlı Kurallar

- Script GUID: `.meta` dosyalarını kaybetme. Script'in GUID'i değişirse prefab'ta Missing MonoBehaviour oluşur.
- Field Rename: Serileştirilmiş alanları yeniden adlandırırken `FormerlySerializedAs` kullan; yoksa veri kaybolur.
- Field Type: Serileştirilmiş alan tipini değiştirme; yerine yeni alan ekleyip migration yap.
- [SerializeField]: Serileşmesini istediğin alanlardan kaldırma.
- SerializeReference: `[SerializeReference]` grafiğinde kullanılan tipleri yeniden adlandırma/taşıma.
- UnityEvent: Listener metod imzasını veya event parametre tiplerini değiştirme.
- Prefab Children: Alanların referans verdiği child objeleri silip yeniden oluşturma; ID'ler değişir ve referanslar kırılır.
- Root Canvas: Root `Canvas` yerine Canvas'sız child-UI prefab kullan ve sahnedeki Canvas altına parent et.
- Address Keys: String address yerine `AssetReference<T>` tercih et; key değişse bile yükleme kırılmaz.
- Labels/Groups: Loader bir label'a göre çalışıyorsa, label'ı kaldırma/yeniden adlandırma (loader'ı da güncellemeden).


## Güvenli Field Rename

Yeniden adlandırmada veriyi korumak için `FormerlySerializedAs` kullan.

```csharp
using UnityEngine;
using UnityEngine.Serialization;

public class MyBehaviour : MonoBehaviour
{
    [FormerlySerializedAs("myValue")] // old name
    [SerializeField] private int myNewValue; // new name
}
```


## Tip Değiştirmek Yerine Migration

Bir alanın tipini değiştirmek yerine yeni alan ekleyip tek seferlik migration yap.

```csharp
public class MyBehaviour : MonoBehaviour
{
    [SerializeField] private int speedOld; // keep for migration
    [SerializeField] private float speed;   // new field

    [ContextMenu("Migrate Speed")]
    void Migrate()
    {
        if (speed == 0f && speedOld != 0) speed = speedOld;
    # Addressables Prefab & Serialization — El Kitabı

    Güvenle değiştir, kırmadan güncelle. (Teknik terimler İngilizce, açıklamalar Türkçe)

    ## 🚀 Özellikler

    - Remote catalog loader: `AddressableLoader` ile `baseUrlRoot` + platform + version otomatik çözümleme
    - Label-based load/instantiate: `labelToLoad` ve `instantiate` ile hızlı sahne entegrasyonu
    - UI sprite restore: `AddressableImage` ile Image.sprite runtime’da yüklenir
    - Version discovery: `latest_catalog.txt` veya dizin taraması ile son `catalog_*.json`
    - Retry/backoff: Ağ hatalarında `retryCount` + exponential backoff
    - Smart build uyumu: `SmartAddressablesBuilder` ile marker (`latest_catalog.txt`) ve düzenli klasörler

    ## 📦 Kurulum

    - Unity 2021.3+ (önerilen) ve `com.unity.addressables`
    - Bu repodaki scriptler:
      - `Assets/Scripts/AddressableLoader.cs`
      - `Assets/Scripts/AddressableImage.cs`
      - `Assets/Editor/SmartAddressablesBuilder.cs`

    ## ⚡ Hızlı Başlangıç

    1) Build (Remote) — `SmartAddressablesBuilder` ile grup/label ayarla, export et
    2) HTTP servis:

    ```powershell
    cd C:\\Users\\Gultekin\\Desktop\\AdressablesFolder
    python -m http.server 8000
    ```

    3) Sahne — `AddressableLoader` ekle:

    - `baseUrlRoot = http://localhost:8000/ServerData`
    - Sadece katalog: `labelToLoad` boş, `instantiate=false`
    - Prefabları otomatik yükle: `labelToLoad = cdn`, `instantiate=true`

    4) UI — Image üzerinde `AddressableImage` kullan (opsiyonel)

    - `spriteReference (AssetReferenceSprite)` veya `addressKey` doldur

    ## 🔧 Kullanım Örnekleri

    Addressables ile Prefab instantiate (AssetReference):

    ```csharp
    using UnityEngine;
    using UnityEngine.AddressableAssets;
    using UnityEngine.ResourceManagement.AsyncOperations;

    public class UiLoader : MonoBehaviour
    {
        public AssetReferenceGameObject uiPrefab;
        AsyncOperationHandle<GameObject> _h;

        void Start()
        {
            _h = uiPrefab.InstantiateAsync(parent: transform);
            _h.Completed += h => {
                if (h.Status != AsyncOperationStatus.Succeeded)
                    Debug.LogError($"Instantiate failed: {h.OperationException}");
            };
        }

        void OnDestroy()
        {
            if (_h.IsValid()) Addressables.Release(_h);
        }
    }
    ```

    UI Image sprite’ı runtime’da yüklemek (`AddressableImage`):

    ```csharp
    // Component: AddressableImage (Image üzerinde)
    // spriteReference (AssetReferenceSprite) veya addressKey ver
    ```

    ## 🛡️ En İyi Pratikler (Do/Don’t)

    - Script GUID: `.meta` kaybetme → GUID değişir → Missing MonoBehaviour
    - Field Rename: `FormerlySerializedAs` kullan; doğrudan rename veri kaybettirir
    - Field Type: Tip değiştirme — yerine yeni alan + migration
    - [SerializeField]: Serileşecek alanlardan kaldırma
    - SerializeReference: Class/namespace/assembly adını sabit tut
    - UnityEvent: Parametre/imza değiştirme; inspector bağları bozulur
    - Property: Auto-property serileşmez; backing field kullan
    - Prefab Child: Sil-yarat yapma; rename veya runtime fallback kullan
    - Address Key: String key yerine `AssetReference<T>` tercih et

    ## 🧰 Problem Giderme (Common Errors & Fixes)

    1) “Catalog URL could not be resolved.”

    - `baseUrlRoot` yanlış kök: `ServerData` iki kez eklenmiş olabilir
    - Dizin listeleme yok → `latest_catalog.txt` ekle
    - `appendPlatformFolder/pickLatestVersion` kapatıp doğrudan katalog klasörüne işaret et

    2) “Image beyaz kare”

    - Sahne referansı kopuk; `AddressableImage` ile runtime yükle
    - Prefab dependency’si build’e girmemişse label/grup ayarını kontrol et

    3) “Missing MonoBehaviour”

    - Script `.meta` kaybı/ GUID değişimi; VCS’te `.meta`’yı koru

    4) “Label ile yüklenmedi”

    - Label yanlış/kaldırılmış; Groups’ta entry’yi ve label’ı doğrula

    ## 🔍 Doğrulama & Analiz

    - Addressables → Analyze → Build Layout / Bundle Layout Preview
    - Çıktı: `Library/com.unity.addressables/aa/<platform>/BuildLayout.txt`
    - Groups penceresi: Prefab entry ve label kontrolü

    ## 🔄 Content Update (Özet)

    1. Güvenli değişiklik (`FormerlySerializedAs` tercih)
    2. Prepare for Content Update (content state seç)
    3. Update a Previous Build
    4. Yeni `catalog_*.json` ve asset’leri remote’a yükle

    ## ✅ Hızlı Kontrol Listesi

    - `.meta` VCS’te mi? (GUID sabit)
    - `FormerlySerializedAs` kullanıldı mı?
    - `AssetReference<T>` tercih edildi mi?
    - Prefab child referansları korunuyor mu?
    - Remote `catalog_*.json` erişilebilir mi?
    - Label/Group ayarları loader ile uyumlu mu?

    ---

    Şüphedeysen: Tip değiştirme → hayır; child sil-yarat → hayır. `FormerlySerializedAs` ile rename, `AssetReference<T>` ile yükleme, `.meta` koruma ile sorunsuz güncelle.

