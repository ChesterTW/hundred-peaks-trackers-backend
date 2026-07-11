# 後端遷移設計：Firebase → ASP.NET Core (Backend Migration)

- 日期：2026-07-11
- 方案：ASP.NET Core (.NET 9) MVC Controller + Firestore（維持現況）+ Google OAuth/JWT Auth + Cloud Run
- 範圍：取代現有 Firebase **Authentication**（登入）與前端直連 Firestore 的架構；**Firestore 本身維持作為資料儲存**，改由後端 API 代理存取
- **取代文件**：本文件取代 `2026-06-29-cross-device-sync-design.md` 作為 Trip 資料同步/後端方案的定案來源。舊文件描述的 Firebase 方案目前仍在production 使用中，屬於本次遷移的「現況」而非「目標」。

## 1. 背景與動機

現況（`2026-06-29-cross-device-sync-design.md` 已實作）：Trip 資料存在 Firestore，登入用 Firebase Authentication（Google 登入），跨裝置同步已經在 production 運作。

決定改用自建 ASP.NET Core 後端取代 Firebase **Authentication + 前端直連 Firestore** 的架構，**主要動機是求職展示**：這是一個面試用 side project，Firebase 屬於 BaaS（後端即服務），無法展示「設計並實作一個後端 API」的能力；自己寫 ASP.NET Core + Controller 層，才能對應面試官想看到的後端經驗（尤其對齊目標公司的 .NET 技術棧）。

功能面不是動機——Firebase 方案本身沒有已知問題，這是一次「為了展示技能而做」的技術替換，不是修 bug 或補功能。

**已知取捨（2026-07-11 與使用者確認）**：資料庫維持 Firestore、不搬到 PostgreSQL/Neon，為了省下資料遷移的力氣。代價是**放棄了 EF Core + 關聯式資料庫設計這塊技能展示**——這原本是這次遷移最主要想練的東西之一，也是面試常考的後端能力。這是刻意的權衡，不是遺漏。

## 2. 現況：既有 Firebase 實作

遷移前要先清楚現在系統長什麼樣子，之後才知道要動哪些檔案：

| 檔案                      | 職責                                                                                                                                     |
| ------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `src/firebase.ts`         | 初始化 Firebase app，匯出 `auth` / `db` 實例                                                                                             |
| `src/stores/authStore.ts` | 登入狀態管理：`user`、`login()`（Google `signInWithPopup`）、`logout()`、`onAuthStateChanged` 監聽                                       |
| `src/stores/tripStore.ts` | UI 唯一讀的 trip 來源；依登入狀態切換 localStorage / 雲端資料來源，處理 login migration（localStorage → 雲端 union merge）與 logout 清空 |
| `src/tripSync.ts`         | Firestore CRUD、`onSnapshot` 訂閱、merge 邏輯（union by id + `updatedAt` LWW + 軟刪墓碑）                                                |

資料結構（`Trip`）已包含 `updatedAt`（LWW 用時間戳）與 `deleted`（軟刪墓碑），這兩個欄位是為了 Firestore 同步設計的，遷移到自建後端時要重新檢視是否還需要（後端若用資料庫交易 + REST API，衝突解決策略可能不同，例如伺服器單一權威來源就不需要 client-side LWW）。

## 3. 新架構：已定案的決策

| 層面   | 選擇                                                                  | 決策理由                                                                                                                                                                                                                                                                             |
| ------ | --------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| 框架   | ASP.NET Core (.NET 9) **MVC Controller**（原規劃 Minimal API，已調整） | 目標面試公司 codebase 是傳統 Controller-based，改用 Controller 直接練到面試會用到的寫法（`[ApiController]`、`[HttpGet]` 等 attribute routing、model binding、action filter），比 Minimal API 更貼近實際會被考的技能。程式碼量比 Minimal API 多（每個資源要多寫一個 class），但這裡優先度是「對齊面試」而非「省樣板碼」                                                     |
| ORM    | 不使用（維持 Firestore，非關聯式資料庫）                              | EF Core 沒有官方 Firestore 支援，改用 `Google.Cloud.Firestore` .NET SDK 在後端直接操作。**已知取捨**：放棄關聯式資料庫/EF Core 這塊技能展示，換取不用做資料遷移，見第 1 節                                                                                                              |
| 資料庫 | **Firestore（維持現況，不搬 Neon/PostgreSQL）**                       | 省下資料遷移力氣：既有 production 資料不用搬動，也不用另外申請/管理 Neon。後端改用服務帳號（Service Account）透過 `Google.Cloud.Firestore` SDK 存取，取代前端直連 SDK                                                                                                                  |
| Auth   | **只做 Google OAuth**，不做 email/password，**不用 ASP.NET Identity** | 維持現有登入體驗（現況就是 Google 一鍵登入，不倒退）；email/password 對單一使用者 app 是無需求的功能，不做才符合 YAGNI；ASP.NET Identity 的密碼雜湊/忘記密碼等機制整套用不到，故不引入，改用輕量的自建 User 表 + JWT                                                                 |
| 部署   | Cloud Run（容器化）                                                   | Serverless、流量小時接近免費，不用管 VM 開關機                                                                                                                                                                                                                                       |

### 3.1 Auth 設計細節

流程（前端拿 Google token → 後端驗證 → 後端發自己的 JWT，不直接把 Google token 當 API 憑證）：

```text
前端：Google Identity Services SDK 登入 → 拿到 Google ID Token
    → POST /api/auth/google { idToken }
        → 後端：用 Google.Apis.Auth 的 GoogleJsonWebSignature.ValidateAsync() 驗證 ID Token
            → 查/建 Users 表（用 GoogleSub 對應）
                → 後端簽發自己的 JWT，回傳給前端
                    → 之後打 Trip API 都帶這個 JWT（Authorization: Bearer）
```

`Users` 資料結構見 7.1（DB 維持 Firestore，非關聯式表）。

**不做多 provider（Facebook / LINE）的通用設計**：一般會用 `ExternalLogins(Provider, ProviderUserId, UserId)` 這種通用表結構，讓以後加其他登入方式不用改 schema。但這裡不這麼做——因為是單一使用者 app，資料庫永遠只有一列 User，就算以後真的要加 LINE 登入，改 schema、重新登入一次成本趨近於零，不需要為了假設性的多 provider 需求先做通用化。真的要加時，是一段全新的工作（該 provider 的 token 驗證邏輯），到時候再做。

## 4. 尚待決定事項

無（Trip API 設計已於第 7 節定案）。

## 5. 遷移範圍與風險

改用 Firestore 維持現況後，**不需要資料遷移**（既有 production 資料留在原地），範圍縮小為「前端改接新後端 API」+「後端改接 Firestore」：

- **前端改動**：`authStore.ts` 要從 Firebase Auth SDK 改成 Google Identity Services SDK 登入 + 呼叫 `/api/auth/google` 換自己的 JWT（見 3.1）；`tripStore.ts` / `tripSync.ts` 的 Firestore CRUD 要改成呼叫新後端 REST API
- **即時同步（onSnapshot）決定不保留（2026-07-11 與使用者確認）**：現況前端用 `onSnapshot` 直接訂閱 Firestore，改走後端 REST API 後會失去即時推播，退化成「需手動重新整理才會看到其他裝置的最新資料」。**接受這個退化**：單一使用者同時開多裝置盯著看即時更新的機率低，不值得為此另外做 polling 或 SignalR。連帶影響：`Trip` 的 `updatedAt`/`deleted` 這套 client-side LWW 合併邏輯，在沒有即時多端寫入衝突的前提下可能可以簡化，於 Trip API 設計時一併重新評估（見下方）
- **後端 Firestore 存取憑證**：Cloud Run 上的 ASP.NET Core 要用 Google Cloud Service Account 存取 Firestore，需要設定 Application Default Credentials 或掛載 service account key，屬於部署前要準備的項目
- **`updatedAt` / `deleted` 欄位**：這兩個欄位是為前端 client-side LWW 同步設計的。現在資料庫還是 Firestore，但衝突解決邏輯搬到後端後，這兩個欄位是否還需要維持原本的語意，待 Trip schema 設計時一併決定
- **CLAUDE.md 技術選型表待更新**：目前 `.claude/CLAUDE.md` 仍寫「資料儲存：localStorage（不做後端）」，跟現況（已用 Firebase）及新方向都不一致，建議整個後端方向定案後一併更新，本文件不處理

## 7. Trip API 設計

### 7.1 重要前提：既有資料路徑對應

現有 Firestore 資料路徑是 `users/{firebaseAuthUid}/trips/{tripId}`，這個 `firebaseAuthUid` 是 Firebase Authentication 幫 Google 帳號產生的內部 ID，**不等於** Google OAuth 的 `sub`。改用自建 Google OAuth 驗證後，後端只拿得到 Google 的 `sub`，若直接拿 `sub` 當路徑，會指向一個全新的空 collection，讓既有行程紀錄變得讀不到（資料還在，只是路徑對不上）。

解法：`Users` collection 要多存一個 `firestoreUid` 欄位，記住既有的 `firebaseAuthUid`，讓後端知道該讀哪個路徑。因為是單一使用者，這個值手動查一次（Firebase Console → Authentication → 找到自己帳號的 UID）填進去即可，不需要自動化查找機制。

`Users` collection schema（取代 3.1 原本假設的關聯式 `Users` 表）：

```text
users/{googleSub}          <- 以 Google sub 為 key
  email: string
  firestoreUid: string     <- 既有 Firebase Auth 產生的 uid，指向 users/{firestoreUid}/trips
```

### 7.2 Endpoint 總覽

| Method | Path | 說明 | 驗證 |
| --- | --- | --- | --- |
| POST | `/api/auth/google` | Google 登入，換取後端 JWT（見 3.1） | 不需 JWT |
| GET | `/api/trips` | 取得目前使用者的所有行程 | 需要 JWT |
| GET | `/api/trips/{id}` | 取得單筆行程 | 需要 JWT |
| POST | `/api/trips` | 新增行程 | 需要 JWT |
| PUT | `/api/trips/{id}` | 更新行程 | 需要 JWT |
| DELETE | `/api/trips/{id}` | 刪除行程（真刪除，見 7.3） | 需要 JWT |

### 7.3 `updatedAt` / `deleted` 簡化結果

依第 5 節「不保留即時同步」的決定，重新評估這兩個欄位：

- **`deleted`（軟刪墓碑）：拿掉**。這是為了「離線多端寫入」情境下用墓碑避免刪除被復活；現在後端是資料的單一權威來源，DELETE 直接做真刪除即可。
- **`updatedAt`：保留，但改為伺服器端自動寫入、對前端唯讀**。不再用於 LWW 合併判斷，純粹是「最後更新時間」的顯示用途。前端 request body 不用送這個欄位。

### 7.4 Schema

```csharp
// Response
public class TripDto
{
    public string Id { get; set; } = default!;
    public string Date { get; set; } = default!;   // "YYYY-MM-DD"
    public List<int> PeakIds { get; set; } = new();
    public string? Note { get; set; }
    public long UpdatedAt { get; set; }             // 伺服器寫入時的 Unix ms 時間戳，唯讀
}

// Request（新增/更新共用）
public class TripRequest
{
    [Required]
    public string Date { get; set; } = default!;

    [Required, MinLength(1)]
    public List<int> PeakIds { get; set; } = new();

    public string? Note { get; set; }
}
```

### 7.5 Controller 骨架

```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TripsController : ControllerBase
{
    private readonly FirestoreDb _db;
    private readonly ICurrentUserService _currentUser; // 從 JWT claims 解析出 Users collection 對應的 firestoreUid

    public TripsController(FirestoreDb db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private CollectionReference Trips =>
        _db.Collection($"users/{_currentUser.FirestoreUid}/trips");

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TripDto>>> GetTrips()
    {
        var snapshot = await Trips.GetSnapshotAsync();
        return Ok(snapshot.Documents.Select(MapToDto));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TripDto>> GetTrip(string id)
    {
        var doc = await Trips.Document(id).GetSnapshotAsync();
        if (!doc.Exists) return NotFound();
        return Ok(MapToDto(doc));
    }

    [HttpPost]
    public async Task<ActionResult<TripDto>> CreateTrip(TripRequest request)
    {
        var id = Guid.NewGuid().ToString();
        var updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        await Trips.Document(id).SetAsync(new Dictionary<string, object?>
        {
            ["date"] = request.Date,
            ["peakIds"] = request.PeakIds,
            ["note"] = request.Note,
            ["updatedAt"] = updatedAt,
        });

        var dto = new TripDto { Id = id, Date = request.Date, PeakIds = request.PeakIds, Note = request.Note, UpdatedAt = updatedAt };
        return CreatedAtAction(nameof(GetTrip), new { id }, dto);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<TripDto>> UpdateTrip(string id, TripRequest request)
    {
        var docRef = Trips.Document(id);
        var doc = await docRef.GetSnapshotAsync();
        if (!doc.Exists) return NotFound();

        var updatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await docRef.SetAsync(new Dictionary<string, object?>
        {
            ["date"] = request.Date,
            ["peakIds"] = request.PeakIds,
            ["note"] = request.Note,
            ["updatedAt"] = updatedAt,
        }, SetOptions.Overwrite);

        return Ok(new TripDto { Id = id, Date = request.Date, PeakIds = request.PeakIds, Note = request.Note, UpdatedAt = updatedAt });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTrip(string id)
    {
        var docRef = Trips.Document(id);
        var doc = await docRef.GetSnapshotAsync();
        if (!doc.Exists) return NotFound();

        await docRef.DeleteAsync();
        return NoContent();
    }

    private static TripDto MapToDto(DocumentSnapshot doc) => new()
    {
        Id = doc.Id,
        Date = doc.GetValue<string>("date"),
        PeakIds = doc.GetValue<List<int>>("peakIds"),
        Note = doc.ContainsField("note") ? doc.GetValue<string?>("note") : null,
        UpdatedAt = doc.GetValue<long>("updatedAt"),
    };
}
```

`ICurrentUserService` 是一個小型抽象：從 JWT 的 claims 拿到 `googleSub`，查 `Users` collection 取得對應的 `FirestoreUid`。實作時機留到寫 Auth middleware 那一步一併處理，這裡先只定義介面用途。

## 8. 下一步

1. 建 ASP.NET Core (.NET 9) MVC 專案骨架，設定 JWT Authentication middleware
2. 在 Google Cloud Console 註冊 OAuth Client ID，實作 `/api/auth/google`（3.1）
3. 手動查現有 Firebase Auth 的 `firebaseAuthUid`，寫進 `Users` collection 的 `firestoreUid` 欄位（7.1）
4. 實作 `TripsController`（7.5），本機測試 CRUD
5. 前端改接：`authStore.ts` 換 Google Identity Services SDK，`tripStore.ts`/`tripSync.ts` 改呼叫新後端 REST API
6. 部署到 Cloud Run，設定 Service Account 存取 Firestore 權限
