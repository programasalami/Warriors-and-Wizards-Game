namespace WaWClient.Networking.Structs;

public interface IDataObject {
    public void Reset();
    public void Read(ref SpanReader reader);
    public void Write(ref SpanWriter writer);
}