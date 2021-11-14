    public interface ICustomInterner
    {
        string WeakIntern(string str);
        string WeakIntern(ReadOnlySpan<char> str);
    }