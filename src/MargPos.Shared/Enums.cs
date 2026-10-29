namespace MargPos.Shared;

public enum UserRole { Cashier = 0, Manager = 1, Admin = 2 }

/// <summary>Pricing scheme applied at billing (mirrors MARG SALETYPE).</summary>
public enum CustomerType { Retail = 0, Wholesale = 1, Hospital = 2 }

/// <summary>Drug schedule under the Drugs and Cosmetics Rules; drives prescription prompts at billing.</summary>
public enum DrugSchedule { None = 0, H = 1, H1 = 2, X = 3 }
