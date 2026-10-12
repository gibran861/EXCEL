public class FlowViewDto
{
    public string FLOW_CODE { get; set; }
    public string DESCRIPTION { get; set; }

    // 💡 CORRECTION : Changer 'int' par 'byte' pour correspondre au type 'tinyint' de SQL Server
    public byte ? DIRECTION { get; set; } 

    public string? SENS { get; set; } // 'C' ou 'D'
}