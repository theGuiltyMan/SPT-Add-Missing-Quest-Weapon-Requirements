import fs from "fs";
import { jsonc } from "jsonc";
import path from "path";

const safejsonc = jsonc.safe;
/**
 * Reads and parses a JSON or JSONC file from the specified file path.
 * 
 * @template T - The type of the parsed JSON object.
 * @param {string} filePath - The path to the directory containing the file.
 * @param {string} fileName - The name of the file (without the extension).
 * @returns {T} - The parsed JSON object.
 * @throws {Error} - If the file is not found.
 */
import { LogHelper } from "./logHelper";

export function tryReadJson<T>(filePath: string, fileName: string, logger: LogHelper): T
{
    let [err, obj] = safejsonc.readSync( path.resolve(filePath, `${fileName}.jsonc`));

    if (!err) 
    {
        return obj as T;
    }

    // (err as any) to shut incorrect error
    if ((err as any).code !== "ENOENT") 
    {
        logger.error(`Error reading ${fileName}.jsonc: ${err.message}`);
    }

    [err, obj] = safejsonc.readSync( path.resolve(filePath, `${fileName}.json`));
    if (!err) 
    {
        return obj as T;
    }

    if ((err as any).code !== "ENOENT") 
    {
        logger.error(`Error reading ${fileName}.json: ${err.message}`);
    }
    else 
    {
        logger.log(`File ${fileName} not found in ${filePath}. Skipping...`);
    }

    return null;
}

export function readJson<T>(filePath: string): T 
{
    return jsonc.parse(fs.readFileSync(filePath).toString());
}

