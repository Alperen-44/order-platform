namespace OrderPlatform.Messaging.Consuming;

/// <summary>
/// Kaç kez denenirse denensin işlenemeyecek mesaj ("zehirli mesaj"): bozuk JSON, eksik başlık vb.
/// Tekrar denemek boşuna olduğu için bu hata alınan mesaj doğrudan Dead Letter Queue'ya gider.
/// </summary>
public sealed class PoisonMessageException(string message, Exception? inner = null)
    : Exception(message, inner);
