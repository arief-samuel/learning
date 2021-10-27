namespace GlobalExceptionHandlingInASPNETCore
{
    public class ApiResponse<T>
    {
        public T Data { get; set; }
        
        public bool Succeeded { get; set; }
        
        public string Message { get; set; }
        
        public static ApiResponse<T> Fail(string error)
        {
            return new ApiResponse<T> {Succeeded = false, Message = error};
        }

        public static ApiResponse<T> Success(T data)
        {
            return new ApiResponse<T> {Succeeded = true, Data = data};
        }
    }
}