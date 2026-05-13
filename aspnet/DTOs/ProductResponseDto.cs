namespace DemoAPI.DTOs
{
    public class ProductResponseDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int Price { get; set; }
        public int Stock { get; set; }
        public int UserId { get; set; }

        // 加工欄位
        public string FormattedPrice => $"${Price}";
    }
}