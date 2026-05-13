namespace DemoAPI.Helpers
{
    public class ApiResponse<T>
    {
        public string Status { get; set; } = "success";
        public string? Message { get; set; }
        public T? Data { get; set; }
    }
}