using System.ComponentModel.DataAnnotations;

namespace AutoPartsWeb.Models
{
    public record AuthLoginRequest([Required, EmailAddress] string Email, [Required] string Password);
    public record AuthRegisterRequest([Required] string FullName, [Required, EmailAddress] string Email, [Required] string Password);
    public record ConfirmEmailRequest([Required, EmailAddress] string Email, [Required] string Token);
    public record ResendConfirmRequest([Required, EmailAddress] string Email);
    public record ForgotPasswordRequest([Required, EmailAddress] string Email);
    public record ResetPasswordRequest([Required, EmailAddress] string Email, [Required] string Token, [Required] string Password);
    public record UpdateProfileRequest([Required] string FullName, [Required, EmailAddress] string Email);
    public record AuthResponseDto(int Id, string FullName, string Email, string Role, string Token);

    public record VehicleDto(
        int Id,
        string Brand,
        string Model,
        int Year,
        string? Engine,
        int? StartYear,
        int? EndYear,
        string? ImageUrl,
        string? BrandLogoUrl);
    public record PartImageDto(int Id, string Url, int SortOrder);

    public record PartListDto(
        int Id,
        string Name,
        string Brand,
        string Category,
        decimal Price,
        int Stock,
        string? ImageUrl,
        double Rating,
        int RatingCount,
        List<VehicleDto> Vehicles,
        string? SellerName);

    public record QuestionDto(
        int Id,
        string Question,
        string? Answer,
        string? UserName,
        DateTime CreatedAt,
        DateTime? AnsweredAt);

    public record ReviewDto(
        int Id,
        int Rating,
        string? Comment,
        string? UserName,
        DateTime CreatedAt);

    public record PartDetailDto(
        int Id,
        string Name,
        string Brand,
        string Category,
        decimal Price,
        int Stock,
        string? Description,
        string? ImageUrl,
        int? SellerId,
        string? SellerName,
        List<VehicleDto> Vehicles,
        List<PartImageDto> Images,
        double Rating,
        int RatingCount,
        List<QuestionDto> Questions,
        List<ReviewDto> Reviews);

    public record PartsFilterMetaDto(
        List<string> Categories,
        List<string> PartBrands,
        List<VehicleDto> Vehicles,
        decimal MinPrice,
        decimal MaxPrice);

    public record QuestionCreateRequest([Required] string Question);
    public record ReviewCreateRequest([Range(1, 5)] int Rating, string? Comment);

    public record OrderItemCreateRequest(int PartId, int Quantity);
    public record OrderCreateRequest(
        [Required] string CustomerName,
        [Required] string Email,
        [Required] string Address,
        string? City,
        string? Phone,
        List<OrderItemCreateRequest> Items);

    public record OrderItemDto(int PartId, string PartName, int Quantity, decimal UnitPrice, string? ImageUrl)
    {
        public OrderItemDto(int PartId, string PartName, int Quantity, decimal UnitPrice)
            : this(PartId, PartName, Quantity, UnitPrice, null)
        {
        }
    }
    public record OrderDto(
        int Id,
        DateTime CreatedAt,
        string Status,
        decimal Total,
        string CustomerName,
        string Email,
        string? Address,
        string? City,
        string? Phone,
        List<OrderItemDto> Items);

    public record SellerApplyRequest(
        [Required] string FullName,
        [Required, EmailAddress] string Email,
        [Required] string Password,
        [Required] string CompanyName,
        [Required] string Phone,
        [Required] string Address,
        string? TaxNumber,
        string? Note);

    public record PartCreateRequest(
        [Required, StringLength(120)] string Name,
        [Required, StringLength(60)] string Brand,
        [Required, StringLength(40)] string Category,
        [Range(0, 999999)] decimal Price,
        [Range(0, 999999)] int Stock,
        string? Description,
        int[]? VehicleIds);

    public record SellerOrderDto(int OrderId, DateTime CreatedAt, string Status, string CustomerName, decimal Total, List<OrderItemDto> Items);
    public record AnswerQuestionRequest([Required] string Answer);
}
