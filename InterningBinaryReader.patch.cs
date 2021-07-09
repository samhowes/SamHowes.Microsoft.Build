    public interface ICustomInterner
    {
        string CharArrayToString(char[] candidate, int count);
        string StringBuilderToString(StringBuilder candidate);
    }