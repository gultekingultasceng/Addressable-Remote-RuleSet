
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
    # Addressables Prefab & Serialization — Yanlış / Doğru Hızlı Rehber

    Minimum metin, yan yana örneklerle hızlı referans.

    ## Script GUID / .meta
    | Yanlış | Doğru |
    | --- | --- |
    | .meta kaybolur → GUID değişir → Missing MonoBehaviour | .meta korunur (GUID sabit) |
    | ```text
    Script.cs (sil-yarat)
    .meta YOK
    ``` | ```text
    Script.cs
    Script.cs.meta (VCS'te)
    ``` |

    ## Field Rename
    | Yanlış | Doğru |
    | --- | --- |
    | ```csharp
    public int myValue; // rename → data kayıp
    public int myNewValue;
    ``` | ```csharp
    using UnityEngine.Serialization;
    [FormerlySerializedAs("myValue")]
    [SerializeField] private int myNewValue;
    ``` |

    ## Field Type Change
    | Yanlış | Doğru |
    | --- | --- |
    | ```csharp
    [SerializeField] int speed; // int → float
    ``` | ```csharp
    [SerializeField] int speedOld; // migrate
    [SerializeField] float speed;
    [ContextMenu("Migrate")]
    void Migrate(){ if(speed==0&&speedOld!=0) speed=speedOld; }
    ``` |

    ## [SerializeField] Kaldırma
    | Yanlış | Doğru |
    | --- | --- |
    | ```csharp
    private int health; // [SerializeField] kaldırıldı → veri kayıp
    ``` | ```csharp
    [SerializeField] private int health; // serileşmeye devam
    ``` |

    ## SerializeReference
    | Yanlış | Doğru |
    | --- | --- |
    | ```csharp
    // Class/namespace rename
    [SerializeReference] object behavior;
    ``` | ```csharp
    // Tip adını sabit tut (assembly-qualified)
    [SerializeReference] object behavior = new Attack();
    ``` |

    ## UnityEvent İmzası
    | Yanlış | Doğru |
    | --- | --- |
    | ```csharp
    public UnityEvent<float> onScore; // int → float
    ``` | ```csharp
    public UnityEvent<int> onScore; // imza sabit
    ``` |

    ## Property Serileştirme
    | Yanlış | Doğru |
    | --- | --- |
    | ```csharp
    public int Health {get;set;} // auto-prop serileşmez
    ``` | ```csharp
    [SerializeField] int health;
    public int Health { get=>health; set=>health=value; }
    ``` |

    ## Prefab Child Referansları
    | Yanlış | Doğru |
    | --- | --- |
    | Child'ı sil-yarat → ID değişir, referans kırılır | Child'ı koru; gerekiyorsa sadece rename |
    | ```csharp
    public GameObject button; // missing after recreate
    ``` | ```csharp
    void Awake(){ if(button==null)
     button=transform.Find("Header/Button")?.gameObject; }
    ``` |

    ## AssetReference vs String Address
    | Yanlış | Doğru |
    | --- | --- |
    | ```csharp
    Addressables.InstantiateAsync("ui/main"); // key değişirse kırılır
    ``` | ```csharp
    public AssetReferenceGameObject uiPrefab;
    var h = uiPrefab.InstantiateAsync(parent: transform);
    ``` |

    ## UI Image Sprite Binding
    | Yanlış | Doğru |
    | --- | --- |
    | Image.sprite sahne ref → asset silinince beyaz kare | `AddressableImage` ile runtime load |
    |  | ```csharp
    // Component: AddressableImage
    // spriteReference veya addressKey doldur
    ``` |

    ## AddressableLoader (Katalog)
    | Yanlış | Doğru |
    | --- | --- |
    | Yanlış baseUrlRoot (dizin görünmüyor) | Doğru kök + platform klasörü |
    |  | ```text
    http://localhost:8000/ServerData/StandaloneWindows64/
    catalog_*.json veya latest_catalog.txt
    ``` |

    ## Hızlı HTTP Servis
    ```powershell
    cd C:\\Users\\Gultekin\\Desktop\\AdressablesFolder
    python -m http.server 8000
    ```

    ## Bundle/Layout Kontrol
    - Analyze → Build Layout
    - `Library/com.unity.addressables/aa/<platform>/BuildLayout.txt`

    ## Content Update (Özet)
    1) `FormerlySerializedAs` ile güvenli değişiklik
    2) Prepare for Content Update
    3) Update a Previous Build
    4) Yeni `catalog_*.json` ve dosyaları upload

