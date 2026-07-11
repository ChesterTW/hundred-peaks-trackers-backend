using Google.Cloud.Firestore;

namespace HundredPeaksTrackers.Api.Models;

[FirestoreData]
public class UserRecord
{
    [FirestoreProperty("email")]
    public string Email { get; set; } = default!;

    // 既有 Firebase Auth 產生的 uid，指向 users/{firestoreUid}/trips（見設計文件 7.1）
    [FirestoreProperty("firestoreUid")]
    public string FirestoreUid { get; set; } = default!;
}
