using OrderService.Features.Orders;

namespace OrderService.UnitTests;

/// <summary>POST /orders isteğinin doğrulama kuralları.</summary>
public class CreateOrderRequestTests
{
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();

    private static CreateOrderRequest Request(params CreateOrderItemRequest[] items) =>
        new(Customer, items.ToList());

    private static CreateOrderItemRequest Item(int quantity = 1, Guid? productId = null) =>
        new(productId ?? Product, quantity);

    [Fact]
    public void Gecerli_istek_hata_dondurmez()
    {
        var errors = Request(Item(), Item(productId: Guid.NewGuid())).Validate();

        errors.Should().BeEmpty();
    }

    [Fact]
    public void Musteri_bos_olamaz()
    {
        var errors = new CreateOrderRequest(Guid.Empty, [Item()]).Validate();

        errors.Should().ContainKey("customerId");
    }

    [Fact]
    public void Kalem_listesi_gonderilmezse_hata_verir()
    {
        var errors = new CreateOrderRequest(Customer, null).Validate();

        errors.Should().ContainKey("items");
    }

    [Fact]
    public void Kalem_listesi_bossa_hata_verir()
    {
        Request().Validate().Should().ContainKey("items");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Adet_sifirdan_buyuk_olmali(int quantity)
    {
        Request(Item(quantity: quantity)).Validate().Should().ContainKey("items.quantity");
    }

    [Fact]
    public void Urun_bos_olamaz()
    {
        Request(Item(productId: Guid.Empty)).Validate().Should().ContainKey("items.productId");
    }

    [Fact]
    public void Ayni_urun_iki_satirda_olamaz()
    {
        Request(Item(), Item()).Validate().Should().ContainKey("items");
    }

    [Fact]
    public void Birden_fazla_hata_ayni_anda_raporlanir()
    {
        var errors = new CreateOrderRequest(Guid.Empty, [Item(quantity: 0)]).Validate();

        errors.Keys.Should().Contain(new[] { "customerId", "items.quantity" });
    }
}
