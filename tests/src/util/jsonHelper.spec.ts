
import { tryReadJson, readJson } from "../../../src/util/jsonHelper";
import { LogHelper } from "../../../src/util/logHelper";
import { jsonc } from "jsonc";
import fs from "fs";
import path from "path";
import {describe, it, expect} from "@jest/globals"
const safejsonc = jsonc.safe;
jest.mock("jsonc");
jest.mock("fs");

const mockLogger = {
    error: jest.fn(),
    log: jest.fn()
} as unknown as LogHelper;

describe("jsonHelper", () => 
{
    afterEach(() => 
    {
        jest.clearAllMocks();
    });

    describe("tryReadJson", () => 
    {
        it("should read a .jsonc file successfully", () => 
        {
            const data = { key: "value" };
            (safejsonc.readSync as jest.Mock).mockReturnValue([null, data]);

            const result = tryReadJson("/fake/path", "file", mockLogger);

            expect(result).toEqual(data);
            expect(safejsonc.readSync).toHaveBeenCalledWith(path.resolve("/fake/path", "file.jsonc"));
            expect(mockLogger.error).not.toHaveBeenCalled();
        });

        it("should fall back to .json if .jsonc is not found", () => 
        {
            const data = { key: "value" };
            (safejsonc.readSync as jest.Mock)
                .mockReturnValueOnce([{ code: "ENOENT" }, null])
                .mockReturnValueOnce([null, data]);

            const result = tryReadJson("/fake/path", "file", mockLogger);

            expect(result).toEqual(data);
            expect(safejsonc.readSync).toHaveBeenCalledWith(path.resolve("/fake/path", "file.jsonc"));
            expect(safejsonc.readSync).toHaveBeenCalledWith(path.resolve("/fake/path", "file.json"));
            expect(mockLogger.error).not.toHaveBeenCalled();
        });

        it("should log an error if .jsonc reading fails for reasons other than not found", () => 
        {
            const error = new Error("test error");
            (safejsonc.readSync as jest.Mock).mockReturnValue([error, null]);

            tryReadJson("/fake/path", "file", mockLogger);

            expect(mockLogger.error).toHaveBeenCalledWith("Error reading file.jsonc: test error");
        });

        it("should return null and log if both .jsonc and .json are not found", () => 
        {
            (safejsonc.readSync as jest.Mock).mockReturnValue([{ code: "ENOENT" }, null]);

            const result = tryReadJson("/fake/path", "file", mockLogger);

            expect(result).toBeNull();
            expect(mockLogger.log).toHaveBeenCalledWith("File file not found in /fake/path. Skipping...");
        });
    });

    describe("readJson", () => 
    {
        it("should read and parse a json file", () => 
        {
            const fileContent = "{ \"key\": \"value\" }";
            const parsed = { key: "value" };
            (fs.readFileSync as jest.Mock).mockReturnValue(fileContent);
            (jsonc.parse as jest.Mock).mockReturnValue(parsed);

            const result = readJson("/fake/path/file.jsonc");

            expect(result).toEqual(parsed);
            expect(fs.readFileSync).toHaveBeenCalledWith("/fake/path/file.jsonc");
            expect(jsonc.parse).toHaveBeenCalledWith(fileContent);
        });
    });
});
