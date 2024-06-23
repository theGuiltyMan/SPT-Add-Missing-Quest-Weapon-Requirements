
// Could've used Set, but it'd do be like that sometimes
export function pushIfNotExists<T>(arr: T[], item: T): boolean 
{
    if (arr.indexOf(item) === -1) 
    {
        arr.push(item);
        return true;
    }
    return false;
} 