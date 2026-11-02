namespace BillKro.Shared;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, DateTime ExpiresAt, UserDto User, bool MustChangePassword);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record UserDto(int Id, string Username, string FullName, UserRole Role, bool IsActive);

public record UserCreateRequest(string Username, string FullName, UserRole Role, string Password);

public record UserUpdateRequest(string FullName, UserRole Role, bool IsActive);

public record ResetPasswordRequest(string NewPassword);
