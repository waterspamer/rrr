public interface ICarInputSource
{
    bool TryGetControlFrame(out CarControlFrame frame);
}
