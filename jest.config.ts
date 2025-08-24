import type {Config} from "jest";

const tsconfig = require("./tsconfig.json");
const moduleNameMapper = require("tsconfig-paths-jest")(tsconfig);

const config: Config = {
    preset: "ts-jest",
    testEnvironment: "node",
    moduleDirectories: ["node_modules", "<rootDir>/"],
    moduleFileExtensions: ["ts", "js", "json", "node", "d.ts"],
    testPathIgnorePatterns: ["<rootDir>/node_modules/", "<rootDir>/dist/"],
    // moduleNameMapper: {
    //     "^@spt/(.*)$": "<rootDir>/types/$1"
    // }
    moduleNameMapper: moduleNameMapper
}

export default config;
