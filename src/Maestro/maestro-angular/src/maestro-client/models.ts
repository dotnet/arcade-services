import { parseISO } from "date-fns";

import { Helper } from "./helper";


export enum AddAssetLocationToAssetAssetLocationType {
    None = "none",
    NugetFeed = "nugetFeed",
    Container = "container",
}

export class ApiError {
    public constructor(
        {
            message,
            errors,
        }: {
            message?: string,
            errors?: string[],
        }
    ) {
        this._message = message;
        this._errors = errors;
    }

    private _message?: string;

    public get message(): string | undefined {
        return this._message;
    }

    private _errors?: string[];

    public get errors(): string[] | undefined {
        return this._errors;
    }

    public static fromRawObject(value: any): ApiError {
        let result = new ApiError({
            message: value["message"] == null ? undefined : value["message"] as any,
            errors: value["errors"] == null ? undefined : value["errors"].map((e: any) => e) as any,
        });
        return result;
    }

    public static toRawObject(value: ApiError): any {
        let result: any = {};
        if (value._message) {
            result["message"] = value._message;
        }
        if (value._errors) {
            result["errors"] = value._errors.map((e: any) => e);
        }
        return result;
    }
}

export class Asset {
    public constructor(
        {
            id,
            name,
            version,
            buildId,
            nonShipping,
            locations,
        }: {
            id: number,
            name?: string,
            version?: string,
            buildId: number,
            nonShipping: boolean,
            locations?: AssetLocation[],
        }
    ) {
        this._id = id;
        this._name = name;
        this._version = version;
        this._buildId = buildId;
        this._nonShipping = nonShipping;
        this._locations = locations;
    }

    private _id: number;

    public get id(): number {
        return this._id;
    }

    private _name?: string;

    public get name(): string | undefined {
        return this._name;
    }

    private _version?: string;

    public get version(): string | undefined {
        return this._version;
    }

    private _buildId: number;

    public get buildId(): number {
        return this._buildId;
    }

    public set buildId(__value: number) {
        this._buildId = __value;
    }

    private _nonShipping: boolean;

    public get nonShipping(): boolean {
        return this._nonShipping;
    }

    public set nonShipping(__value: boolean) {
        this._nonShipping = __value;
    }

    private _locations?: AssetLocation[];

    public get locations(): AssetLocation[] | undefined {
        return this._locations;
    }
    
    public isValid(): boolean {
        return (
            this._id !== undefined &&
            this._buildId !== undefined &&
            this._nonShipping !== undefined
        );
    }

    public static fromRawObject(value: any): Asset {
        let result = new Asset({
            id: value["id"] == null ? undefined : value["id"] as any,
            name: value["name"] == null ? undefined : value["name"] as any,
            version: value["version"] == null ? undefined : value["version"] as any,
            buildId: value["buildId"] == null ? undefined : value["buildId"] as any,
            nonShipping: value["nonShipping"] == null ? undefined : value["nonShipping"] as any,
            locations: value["locations"] == null ? undefined : value["locations"].map((e: any) => AssetLocation.fromRawObject(e)) as any,
        });
        return result;
    }

    public static toRawObject(value: Asset): any {
        let result: any = {};
        result["id"] = value._id;
        if (value._name) {
            result["name"] = value._name;
        }
        if (value._version) {
            result["version"] = value._version;
        }
        result["buildId"] = value._buildId;
        result["nonShipping"] = value._nonShipping;
        if (value._locations) {
            result["locations"] = value._locations.map((e: any) => AssetLocation.toRawObject(e));
        }
        return result;
    }
}

export class AssetData {
    public constructor(
        {
            name,
            version,
            nonShipping,
            locations,
        }: {
            name?: string,
            version?: string,
            nonShipping: boolean,
            locations?: AssetLocationData[],
        }
    ) {
        this._name = name;
        this._version = version;
        this._nonShipping = nonShipping;
        this._locations = locations;
    }

    private _name?: string;

    public get name(): string | undefined {
        return this._name;
    }

    public set name(__value: string | undefined) {
        this._name = __value;
    }

    private _version?: string;

    public get version(): string | undefined {
        return this._version;
    }

    public set version(__value: string | undefined) {
        this._version = __value;
    }

    private _nonShipping: boolean;

    public get nonShipping(): boolean {
        return this._nonShipping;
    }

    public set nonShipping(__value: boolean) {
        this._nonShipping = __value;
    }

    private _locations?: AssetLocationData[];

    public get locations(): AssetLocationData[] | undefined {
        return this._locations;
    }

    public set locations(__value: AssetLocationData[] | undefined) {
        this._locations = __value;
    }
    
    public isValid(): boolean {
        return (
            this._nonShipping !== undefined
        );
    }

    public static fromRawObject(value: any): AssetData {
        let result = new AssetData({
            name: value["name"] == null ? undefined : value["name"] as any,
            version: value["version"] == null ? undefined : value["version"] as any,
            nonShipping: value["nonShipping"] == null ? undefined : value["nonShipping"] as any,
            locations: value["locations"] == null ? undefined : value["locations"].map((e: any) => AssetLocationData.fromRawObject(e)) as any,
        });
        return result;
    }

    public static toRawObject(value: AssetData): any {
        let result: any = {};
        if (value._name) {
            result["name"] = value._name;
        }
        if (value._version) {
            result["version"] = value._version;
        }
        result["nonShipping"] = value._nonShipping;
        if (value._locations) {
            result["locations"] = value._locations.map((e: any) => AssetLocationData.toRawObject(e));
        }
        return result;
    }
}

export class AssetLocation {
    public constructor(
        {
            id,
            location,
            type,
        }: {
            id: number,
            location?: string,
            type: AssetLocationType,
        }
    ) {
        this._id = id;
        this._location = location;
        this._type = type;
    }

    private _id: number;

    public get id(): number {
        return this._id;
    }

    private _location?: string;

    public get location(): string | undefined {
        return this._location;
    }

    private _type: AssetLocationType;

    public get type(): AssetLocationType {
        return this._type;
    }
    
    public isValid(): boolean {
        return (
            this._id !== undefined &&
            this._type !== undefined
        );
    }

    public static fromRawObject(value: any): AssetLocation {
        let result = new AssetLocation({
            id: value["id"] == null ? undefined : value["id"] as any,
            location: value["location"] == null ? undefined : value["location"] as any,
            type: value["type"] == null ? undefined : value["type"] as any,
        });
        return result;
    }

    public static toRawObject(value: AssetLocation): any {
        let result: any = {};
        result["id"] = value._id;
        if (value._location) {
            result["location"] = value._location;
        }
        result["type"] = value._type;
        return result;
    }
}

export class AssetLocationData {
    public constructor(
        {
            location,
            type,
        }: {
            location?: string,
            type: AssetLocationDataType,
        }
    ) {
        this._location = location;
        this._type = type;
    }

    private _location?: string;

    public get location(): string | undefined {
        return this._location;
    }

    public set location(__value: string | undefined) {
        this._location = __value;
    }

    private _type: AssetLocationDataType;

    public get type(): AssetLocationDataType {
        return this._type;
    }

    public set type(__value: AssetLocationDataType) {
        this._type = __value;
    }
    
    public isValid(): boolean {
        return (
            this._type !== undefined
        );
    }

    public static fromRawObject(value: any): AssetLocationData {
        let result = new AssetLocationData({
            location: value["location"] == null ? undefined : value["location"] as any,
            type: value["type"] == null ? undefined : value["type"] as any,
        });
        return result;
    }

    public static toRawObject(value: AssetLocationData): any {
        let result: any = {};
        if (value._location) {
            result["location"] = value._location;
        }
        result["type"] = value._type;
        return result;
    }
}

export enum AssetLocationDataType {
    None = "none",
    NugetFeed = "nugetFeed",
    Container = "container",
}

export enum AssetLocationType {
    None = "none",
    NugetFeed = "nugetFeed",
    Container = "container",
}

export class Build {
    public constructor(
        {
            id,
            commit,
            azureDevOpsBuildId,
            azureDevOpsBuildDefinitionId,
            azureDevOpsAccount,
            azureDevOpsProject,
            azureDevOpsBuildNumber,
            azureDevOpsRepository,
            azureDevOpsBranch,
            gitHubRepository,
            gitHubBranch,
            publishUsingPipelines,
            dateProduced,
            channels,
            assets,
            dependencies,
            staleness,
        }: {
            id: number,
            commit?: string,
            azureDevOpsBuildId?: number,
            azureDevOpsBuildDefinitionId?: number,
            azureDevOpsAccount?: string,
            azureDevOpsProject?: string,
            azureDevOpsBuildNumber?: string,
            azureDevOpsRepository?: string,
            azureDevOpsBranch?: string,
            gitHubRepository?: string,
            gitHubBranch?: string,
            publishUsingPipelines: boolean,
            dateProduced: Date,
            channels?: Channel[],
            assets?: Asset[],
            dependencies?: BuildRef[],
            staleness?: number,
        }
    ) {
        this._id = id;
        this._commit = commit;
        this._azureDevOpsBuildId = azureDevOpsBuildId;
        this._azureDevOpsBuildDefinitionId = azureDevOpsBuildDefinitionId;
        this._azureDevOpsAccount = azureDevOpsAccount;
        this._azureDevOpsProject = azureDevOpsProject;
        this._azureDevOpsBuildNumber = azureDevOpsBuildNumber;
        this._azureDevOpsRepository = azureDevOpsRepository;
        this._azureDevOpsBranch = azureDevOpsBranch;
        this._gitHubRepository = gitHubRepository;
        this._gitHubBranch = gitHubBranch;
        this._publishUsingPipelines = publishUsingPipelines;
        this._dateProduced = dateProduced;
        this._channels = channels;
        this._assets = assets;
        this._dependencies = dependencies;
        this._staleness = staleness;
    }

    private _id: number;

    public get id(): number {
        return this._id;
    }

    private _commit?: string;

    public get commit(): string | undefined {
        return this._commit;
    }

    private _azureDevOpsBuildId?: number;

    public get azureDevOpsBuildId(): number | undefined {
        return this._azureDevOpsBuildId;
    }

    public set azureDevOpsBuildId(__value: number | undefined) {
        this._azureDevOpsBuildId = __value;
    }

    private _azureDevOpsBuildDefinitionId?: number;

    public get azureDevOpsBuildDefinitionId(): number | undefined {
        return this._azureDevOpsBuildDefinitionId;
    }

    public set azureDevOpsBuildDefinitionId(__value: number | undefined) {
        this._azureDevOpsBuildDefinitionId = __value;
    }

    private _azureDevOpsAccount?: string;

    public get azureDevOpsAccount(): string | undefined {
        return this._azureDevOpsAccount;
    }

    public set azureDevOpsAccount(__value: string | undefined) {
        this._azureDevOpsAccount = __value;
    }

    private _azureDevOpsProject?: string;

    public get azureDevOpsProject(): string | undefined {
        return this._azureDevOpsProject;
    }

    public set azureDevOpsProject(__value: string | undefined) {
        this._azureDevOpsProject = __value;
    }

    private _azureDevOpsBuildNumber?: string;

    public get azureDevOpsBuildNumber(): string | undefined {
        return this._azureDevOpsBuildNumber;
    }

    public set azureDevOpsBuildNumber(__value: string | undefined) {
        this._azureDevOpsBuildNumber = __value;
    }

    private _azureDevOpsRepository?: string;

    public get azureDevOpsRepository(): string | undefined {
        return this._azureDevOpsRepository;
    }

    public set azureDevOpsRepository(__value: string | undefined) {
        this._azureDevOpsRepository = __value;
    }

    private _azureDevOpsBranch?: string;

    public get azureDevOpsBranch(): string | undefined {
        return this._azureDevOpsBranch;
    }

    public set azureDevOpsBranch(__value: string | undefined) {
        this._azureDevOpsBranch = __value;
    }

    private _gitHubRepository?: string;

    public get gitHubRepository(): string | undefined {
        return this._gitHubRepository;
    }

    public set gitHubRepository(__value: string | undefined) {
        this._gitHubRepository = __value;
    }

    private _gitHubBranch?: string;

    public get gitHubBranch(): string | undefined {
        return this._gitHubBranch;
    }

    public set gitHubBranch(__value: string | undefined) {
        this._gitHubBranch = __value;
    }

    private _publishUsingPipelines: boolean;

    public get publishUsingPipelines(): boolean {
        return this._publishUsingPipelines;
    }

    public set publishUsingPipelines(__value: boolean) {
        this._publishUsingPipelines = __value;
    }

    private _dateProduced: Date;

    public get dateProduced(): Date {
        return this._dateProduced;
    }

    private _channels?: Channel[];

    public get channels(): Channel[] | undefined {
        return this._channels;
    }

    private _assets?: Asset[];

    public get assets(): Asset[] | undefined {
        return this._assets;
    }

    private _dependencies?: BuildRef[];

    public get dependencies(): BuildRef[] | undefined {
        return this._dependencies;
    }

    private _staleness?: number;

    public get staleness(): number | undefined {
        return this._staleness;
    }
    
    public isValid(): boolean {
        return (
            this._id !== undefined &&
            this._publishUsingPipelines !== undefined &&
            this._dateProduced !== undefined
        );
    }

    public static fromRawObject(value: any): Build {
        let result = new Build({
            id: value["id"] == null ? undefined : value["id"] as any,
            commit: value["commit"] == null ? undefined : value["commit"] as any,
            azureDevOpsBuildId: value["azureDevOpsBuildId"] == null ? undefined : value["azureDevOpsBuildId"] as any,
            azureDevOpsBuildDefinitionId: value["azureDevOpsBuildDefinitionId"] == null ? undefined : value["azureDevOpsBuildDefinitionId"] as any,
            azureDevOpsAccount: value["azureDevOpsAccount"] == null ? undefined : value["azureDevOpsAccount"] as any,
            azureDevOpsProject: value["azureDevOpsProject"] == null ? undefined : value["azureDevOpsProject"] as any,
            azureDevOpsBuildNumber: value["azureDevOpsBuildNumber"] == null ? undefined : value["azureDevOpsBuildNumber"] as any,
            azureDevOpsRepository: value["azureDevOpsRepository"] == null ? undefined : value["azureDevOpsRepository"] as any,
            azureDevOpsBranch: value["azureDevOpsBranch"] == null ? undefined : value["azureDevOpsBranch"] as any,
            gitHubRepository: value["gitHubRepository"] == null ? undefined : value["gitHubRepository"] as any,
            gitHubBranch: value["gitHubBranch"] == null ? undefined : value["gitHubBranch"] as any,
            publishUsingPipelines: value["publishUsingPipelines"] == null ? undefined : value["publishUsingPipelines"] as any,
            dateProduced: value["dateProduced"] == null ? undefined : parseISO(value["dateProduced"]) as any,
            channels: value["channels"] == null ? undefined : value["channels"].map((e: any) => Channel.fromRawObject(e)) as any,
            assets: value["assets"] == null ? undefined : value["assets"].map((e: any) => Asset.fromRawObject(e)) as any,
            dependencies: value["dependencies"] == null ? undefined : value["dependencies"].map((e: any) => BuildRef.fromRawObject(e)) as any,
            staleness: value["staleness"] == null ? undefined : value["staleness"] as any,
        });
        return result;
    }

    public static toRawObject(value: Build): any {
        let result: any = {};
        result["id"] = value._id;
        if (value._commit) {
            result["commit"] = value._commit;
        }
        if (value._azureDevOpsBuildId) {
            result["azureDevOpsBuildId"] = value._azureDevOpsBuildId;
        }
        if (value._azureDevOpsBuildDefinitionId) {
            result["azureDevOpsBuildDefinitionId"] = value._azureDevOpsBuildDefinitionId;
        }
        if (value._azureDevOpsAccount) {
            result["azureDevOpsAccount"] = value._azureDevOpsAccount;
        }
        if (value._azureDevOpsProject) {
            result["azureDevOpsProject"] = value._azureDevOpsProject;
        }
        if (value._azureDevOpsBuildNumber) {
            result["azureDevOpsBuildNumber"] = value._azureDevOpsBuildNumber;
        }
        if (value._azureDevOpsRepository) {
            result["azureDevOpsRepository"] = value._azureDevOpsRepository;
        }
        if (value._azureDevOpsBranch) {
            result["azureDevOpsBranch"] = value._azureDevOpsBranch;
        }
        if (value._gitHubRepository) {
            result["gitHubRepository"] = value._gitHubRepository;
        }
        if (value._gitHubBranch) {
            result["gitHubBranch"] = value._gitHubBranch;
        }
        result["publishUsingPipelines"] = value._publishUsingPipelines;
        result["dateProduced"] = value._dateProduced.toISOString();
        if (value._channels) {
            result["channels"] = value._channels.map((e: any) => Channel.toRawObject(e));
        }
        if (value._assets) {
            result["assets"] = value._assets.map((e: any) => Asset.toRawObject(e));
        }
        if (value._dependencies) {
            result["dependencies"] = value._dependencies.map((e: any) => BuildRef.toRawObject(e));
        }
        if (value._staleness) {
            result["staleness"] = value._staleness;
        }
        return result;
    }
}

export class BuildData {
    public constructor(
        {
            commit,
            assets,
            dependencies,
            azureDevOpsBuildId,
            azureDevOpsBuildDefinitionId,
            azureDevOpsAccount,
            azureDevOpsProject,
            azureDevOpsBuildNumber,
            azureDevOpsRepository,
            azureDevOpsBranch,
            gitHubRepository,
            gitHubBranch,
            publishUsingPipelines,
        }: {
            commit: string,
            assets?: AssetData[],
            dependencies?: BuildRef[],
            azureDevOpsBuildId?: number,
            azureDevOpsBuildDefinitionId?: number,
            azureDevOpsAccount: string,
            azureDevOpsProject: string,
            azureDevOpsBuildNumber: string,
            azureDevOpsRepository: string,
            azureDevOpsBranch: string,
            gitHubRepository?: string,
            gitHubBranch?: string,
            publishUsingPipelines: boolean,
        }
    ) {
        this._commit = commit;
        this._assets = assets;
        this._dependencies = dependencies;
        this._azureDevOpsBuildId = azureDevOpsBuildId;
        this._azureDevOpsBuildDefinitionId = azureDevOpsBuildDefinitionId;
        this._azureDevOpsAccount = azureDevOpsAccount;
        this._azureDevOpsProject = azureDevOpsProject;
        this._azureDevOpsBuildNumber = azureDevOpsBuildNumber;
        this._azureDevOpsRepository = azureDevOpsRepository;
        this._azureDevOpsBranch = azureDevOpsBranch;
        this._gitHubRepository = gitHubRepository;
        this._gitHubBranch = gitHubBranch;
        this._publishUsingPipelines = publishUsingPipelines;
    }

    private _commit: string;

    public get commit(): string {
        return this._commit;
    }

    public set commit(__value: string) {
        this._commit = __value;
    }

    private _assets?: AssetData[];

    public get assets(): AssetData[] | undefined {
        return this._assets;
    }

    public set assets(__value: AssetData[] | undefined) {
        this._assets = __value;
    }

    private _dependencies?: BuildRef[];

    public get dependencies(): BuildRef[] | undefined {
        return this._dependencies;
    }

    public set dependencies(__value: BuildRef[] | undefined) {
        this._dependencies = __value;
    }

    private _azureDevOpsBuildId?: number;

    public get azureDevOpsBuildId(): number | undefined {
        return this._azureDevOpsBuildId;
    }

    public set azureDevOpsBuildId(__value: number | undefined) {
        this._azureDevOpsBuildId = __value;
    }

    private _azureDevOpsBuildDefinitionId?: number;

    public get azureDevOpsBuildDefinitionId(): number | undefined {
        return this._azureDevOpsBuildDefinitionId;
    }

    public set azureDevOpsBuildDefinitionId(__value: number | undefined) {
        this._azureDevOpsBuildDefinitionId = __value;
    }

    private _azureDevOpsAccount: string;

    public get azureDevOpsAccount(): string {
        return this._azureDevOpsAccount;
    }

    public set azureDevOpsAccount(__value: string) {
        this._azureDevOpsAccount = __value;
    }

    private _azureDevOpsProject: string;

    public get azureDevOpsProject(): string {
        return this._azureDevOpsProject;
    }

    public set azureDevOpsProject(__value: string) {
        this._azureDevOpsProject = __value;
    }

    private _azureDevOpsBuildNumber: string;

    public get azureDevOpsBuildNumber(): string {
        return this._azureDevOpsBuildNumber;
    }

    public set azureDevOpsBuildNumber(__value: string) {
        this._azureDevOpsBuildNumber = __value;
    }

    private _azureDevOpsRepository: string;

    public get azureDevOpsRepository(): string {
        return this._azureDevOpsRepository;
    }

    public set azureDevOpsRepository(__value: string) {
        this._azureDevOpsRepository = __value;
    }

    private _azureDevOpsBranch: string;

    public get azureDevOpsBranch(): string {
        return this._azureDevOpsBranch;
    }

    public set azureDevOpsBranch(__value: string) {
        this._azureDevOpsBranch = __value;
    }

    private _gitHubRepository?: string;

    public get gitHubRepository(): string | undefined {
        return this._gitHubRepository;
    }

    public set gitHubRepository(__value: string | undefined) {
        this._gitHubRepository = __value;
    }

    private _gitHubBranch?: string;

    public get gitHubBranch(): string | undefined {
        return this._gitHubBranch;
    }

    public set gitHubBranch(__value: string | undefined) {
        this._gitHubBranch = __value;
    }

    private _publishUsingPipelines: boolean;

    public get publishUsingPipelines(): boolean {
        return this._publishUsingPipelines;
    }

    public set publishUsingPipelines(__value: boolean) {
        this._publishUsingPipelines = __value;
    }
    
    public isValid(): boolean {
        return (
            this._commit !== undefined &&
            this._azureDevOpsAccount !== undefined &&
            this._azureDevOpsProject !== undefined &&
            this._azureDevOpsBuildNumber !== undefined &&
            this._azureDevOpsRepository !== undefined &&
            this._azureDevOpsBranch !== undefined &&
            this._publishUsingPipelines !== undefined
        );
    }

    public static fromRawObject(value: any): BuildData {
        let result = new BuildData({
            commit: value["commit"] == null ? undefined : value["commit"] as any,
            assets: value["assets"] == null ? undefined : value["assets"].map((e: any) => AssetData.fromRawObject(e)) as any,
            dependencies: value["dependencies"] == null ? undefined : value["dependencies"].map((e: any) => BuildRef.fromRawObject(e)) as any,
            azureDevOpsBuildId: value["azureDevOpsBuildId"] == null ? undefined : value["azureDevOpsBuildId"] as any,
            azureDevOpsBuildDefinitionId: value["azureDevOpsBuildDefinitionId"] == null ? undefined : value["azureDevOpsBuildDefinitionId"] as any,
            azureDevOpsAccount: value["azureDevOpsAccount"] == null ? undefined : value["azureDevOpsAccount"] as any,
            azureDevOpsProject: value["azureDevOpsProject"] == null ? undefined : value["azureDevOpsProject"] as any,
            azureDevOpsBuildNumber: value["azureDevOpsBuildNumber"] == null ? undefined : value["azureDevOpsBuildNumber"] as any,
            azureDevOpsRepository: value["azureDevOpsRepository"] == null ? undefined : value["azureDevOpsRepository"] as any,
            azureDevOpsBranch: value["azureDevOpsBranch"] == null ? undefined : value["azureDevOpsBranch"] as any,
            gitHubRepository: value["gitHubRepository"] == null ? undefined : value["gitHubRepository"] as any,
            gitHubBranch: value["gitHubBranch"] == null ? undefined : value["gitHubBranch"] as any,
            publishUsingPipelines: value["publishUsingPipelines"] == null ? undefined : value["publishUsingPipelines"] as any,
        });
        return result;
    }

    public static toRawObject(value: BuildData): any {
        let result: any = {};
        result["commit"] = value._commit;
        if (value._assets) {
            result["assets"] = value._assets.map((e: any) => AssetData.toRawObject(e));
        }
        if (value._dependencies) {
            result["dependencies"] = value._dependencies.map((e: any) => BuildRef.toRawObject(e));
        }
        if (value._azureDevOpsBuildId) {
            result["azureDevOpsBuildId"] = value._azureDevOpsBuildId;
        }
        if (value._azureDevOpsBuildDefinitionId) {
            result["azureDevOpsBuildDefinitionId"] = value._azureDevOpsBuildDefinitionId;
        }
        result["azureDevOpsAccount"] = value._azureDevOpsAccount;
        result["azureDevOpsProject"] = value._azureDevOpsProject;
        result["azureDevOpsBuildNumber"] = value._azureDevOpsBuildNumber;
        result["azureDevOpsRepository"] = value._azureDevOpsRepository;
        result["azureDevOpsBranch"] = value._azureDevOpsBranch;
        if (value._gitHubRepository) {
            result["gitHubRepository"] = value._gitHubRepository;
        }
        if (value._gitHubBranch) {
            result["gitHubBranch"] = value._gitHubBranch;
        }
        result["publishUsingPipelines"] = value._publishUsingPipelines;
        return result;
    }
}

export class BuildGraph {
    public constructor(
        {
            builds,
        }: {
            builds: Record<string, Build>,
        }
    ) {
        this._builds = builds;
    }

    private _builds: Record<string, Build>;

    public get builds(): Record<string, Build> {
        return this._builds;
    }
    
    public isValid(): boolean {
        return (
            this._builds !== undefined
        );
    }

    public static fromRawObject(value: any): BuildGraph {
        let result = new BuildGraph({
            builds: value["builds"] == null ? undefined : Helper.mapValues(value["builds"], (v: any) => Build.fromRawObject(v)) as any,
        });
        return result;
    }

    public static toRawObject(value: BuildGraph): any {
        let result: any = {};
        result["builds"] = Helper.mapValues(value._builds, (v: any) => Build.toRawObject(v));
        return result;
    }
}

export class BuildRef {
    public constructor(
        {
            buildId,
            isProduct,
            timeToInclusionInMinutes,
        }: {
            buildId: number,
            isProduct: boolean,
            timeToInclusionInMinutes: number,
        }
    ) {
        this._buildId = buildId;
        this._isProduct = isProduct;
        this._timeToInclusionInMinutes = timeToInclusionInMinutes;
    }

    private _buildId: number;

    public get buildId(): number {
        return this._buildId;
    }

    private _isProduct: boolean;

    public get isProduct(): boolean {
        return this._isProduct;
    }

    private _timeToInclusionInMinutes: number;

    public get timeToInclusionInMinutes(): number {
        return this._timeToInclusionInMinutes;
    }
    
    public isValid(): boolean {
        return (
            this._buildId !== undefined &&
            this._isProduct !== undefined &&
            this._timeToInclusionInMinutes !== undefined
        );
    }

    public static fromRawObject(value: any): BuildRef {
        let result = new BuildRef({
            buildId: value["buildId"] == null ? undefined : value["buildId"] as any,
            isProduct: value["isProduct"] == null ? undefined : value["isProduct"] as any,
            timeToInclusionInMinutes: value["timeToInclusionInMinutes"] == null ? undefined : value["timeToInclusionInMinutes"] as any,
        });
        return result;
    }

    public static toRawObject(value: BuildRef): any {
        let result: any = {};
        result["buildId"] = value._buildId;
        result["isProduct"] = value._isProduct;
        result["timeToInclusionInMinutes"] = value._timeToInclusionInMinutes;
        return result;
    }
}

export class FlowGraph {
    public constructor(
        {
            nodes,
            edges,
        }: {
            nodes: FlowRef[],
            edges: FlowEdge[],
        }
    ) {
        this._nodes = nodes;
        this._edges = edges;
    }

    private _nodes: FlowRef[];
    private _edges: FlowEdge[];

    public get nodes(): FlowRef[] {
        return this._nodes;
    }

    public get edges(): FlowEdge[] {
        return this._edges;
    }
    
    public isValid(): boolean {
        return (
            this._nodes !== undefined && this._edges !== undefined
        );
    }

    public static fromRawObject(value: any): FlowGraph {
        let result = new FlowGraph({
            nodes: value["flowRefs"] == null ? undefined : value["flowRefs"].map((n: any) => FlowRef.fromRawObject(n)) as any,
            edges: value["flowEdges"] == null ? undefined : value["flowEdges"].map((e: any) => FlowEdge.fromRawObject(e)) as any,
        });
        return result;
    }

    public static toRawObject(value: FlowGraph): any {
        let result: any = {};
        result["flowRef"] = value._nodes.map((n: any) => FlowRef.toRawObject(n));
        result["flowEdge"] = value._edges.map((e: any) => FlowEdge.toRawObject(e));
        return result;
    }
}

export class FlowRef {
    public constructor(
        {
            id,
            repository,
            branch,
            officialBuildTime,
            prBuildTime,
            bestCaseTime,
            worstCaseTime,
            onLongestBuildPath,
            goalTime,
        }: {
            id: string,
            repository: string,
            branch: string,
            officialBuildTime: number,
            prBuildTime: number,
            bestCaseTime: number,
            worstCaseTime: number,
            onLongestBuildPath: boolean,
            goalTime: number,
        }
    ) {
        this._id = id;
        this._repository = repository;
        this._branch = branch;
        this._officialBuildTime = officialBuildTime;
        this._prBuildTime = prBuildTime;
        this._bestCaseTime = bestCaseTime;
        this._worstCaseTime = worstCaseTime;
        this._onLongestBuildPath = onLongestBuildPath;
        this._goalTime = goalTime;
    }

    private _id: string;

    public get id(): string {
        return this._id;
    }

    private _repository: string;

    public get repository(): string {
        return this._repository;
    }

    private _branch: string;

    public get branch(): string {
        return this._branch;
    }

    private _officialBuildTime: number;

    public get officialBuildTime(): number {
        return this._officialBuildTime;
    }

    private _prBuildTime: number;

    public get prBuildTime(): number {
        return this._prBuildTime;
    }

    private _bestCaseTime: number;

    public get bestCaseTime(): number {
        return this._bestCaseTime;
    }

    private _worstCaseTime: number;

    public get worstCaseTime(): number {
        return this._worstCaseTime;
    }

    private _onLongestBuildPath: boolean;

    public get onLongestBuildPath(): boolean {
        return this._onLongestBuildPath;
    }

    public _goalTime: number;

    public get goalTime(): number {
        return this._goalTime;
    }
    
    public isValid(): boolean {
        return (
            this._id !== undefined &&
            this._officialBuildTime !== undefined
        );
    }

    public static fromRawObject(value: any): FlowRef {
        let result = new FlowRef({
            id: value["id"] == null ? undefined : value["id"] as any,
            repository: value["repository"] == null ? undefined : value["repository"] as any,
            branch: value["branch"] == null ? undefined : value["branch"] as any,
            officialBuildTime: value["officialBuildTime"] == null ? undefined : value["officialBuildTime"] as any,
            prBuildTime: value["prBuildTime"] == null ? undefined : value["prBuildTime"] as any,
            bestCaseTime: value["bestCasePathTime"] == null ? undefined : value["bestCasePathTime"] as any,
            worstCaseTime: value["worstCasePathTime"] == null ? undefined : value["worstCasePathTime"] as any,
            onLongestBuildPath: value["onLongestBuildPath"] == null ? undefined : value["onLongestBuildPath"] as any,
            goalTime: value["goalTime"] == null ? undefined : value["goalTime"] as any,
        });
        return result;
    }

    public static toRawObject(value: FlowRef): any {
        let result: any = {};
        result["id"] = value._id;
        result["repository"] = value._repository;
        result["branch"] = value._branch;
        result["officialBuildTime"] = value._officialBuildTime;
        result["prBuildTime"] = value._prBuildTime;
        result["bestCasePathTime"] = value._bestCaseTime;
        result["worstCasePathTime"] = value._worstCaseTime;
        result["onLongestBuildPath"] = value._onLongestBuildPath;
        result["goalTime"] = value._goalTime;
        return result;
    }
}

export class FlowEdge {
    public constructor(
        {
            to,
            from,
            onLongestBuildPath,
        }: {
            to: string,
            from: string,
            onLongestBuildPath: boolean,
        }
    ) {
        this._toId = to;
        this._fromId = from;
        this._onLongestBuildPath = onLongestBuildPath;
    }

    private _toId: string;

    public get toId(): string {
        return this._toId;
    }

    private _fromId: string;

    public get fromId(): string {
        return this._fromId;
    }

    private _onLongestBuildPath: boolean;

    public get onLongestBuildPath(): boolean {
        return this._onLongestBuildPath;
    }
    
    public isValid(): boolean {
        return (
            this._toId !== undefined &&
            this._fromId !== undefined
        );
    }

    public static fromRawObject(value: any): FlowEdge {
        let result = new FlowEdge({
            to: value["toId"] == null ? undefined : value["toId"] as any,
            from: value["fromId"] == null ? undefined : value["fromId"] as any,
            onLongestBuildPath: value["onLongestBuildPath"] == null ? undefined : value["onLongestBuildPath"] as any,
        });
        return result;
    }

    public static toRawObject(value: FlowEdge): any {
        let result: any = {};
        result["toId"] = value._toId;
        result["fromId"] = value._fromId;
        return result;
    }
}

export class Channel {
    public constructor(
        {
            id,
            name,
            classification,
            releasePipelines,
        }: {
            id: number,
            name?: string,
            classification?: string,
            releasePipelines?: ReleasePipeline[],
        }
    ) {
        this._id = id;
        this._name = name;
        this._classification = classification;
        this._releasePipelines = releasePipelines;
    }

    private _id: number;

    public get id(): number {
        return this._id;
    }

    private _name?: string;

    public get name(): string | undefined {
        return this._name;
    }

    private _classification?: string;

    public get classification(): string | undefined {
        return this._classification;
    }

    private _releasePipelines?: ReleasePipeline[];

    public get releasePipelines(): ReleasePipeline[] | undefined {
        return this._releasePipelines;
    }
    
    public isValid(): boolean {
        return (
            this._id !== undefined
        );
    }

    public static fromRawObject(value: any): Channel {
        let result = new Channel({
            id: value["id"] == null ? undefined : value["id"] as any,
            name: value["name"] == null ? undefined : value["name"] as any,
            classification: value["classification"] == null ? undefined : value["classification"] as any,
            releasePipelines: value["releasePipelines"] == null ? undefined : value["releasePipelines"].map((e: any) => ReleasePipeline.fromRawObject(e)) as any,
        });
        return result;
    }

    public static toRawObject(value: Channel): any {
        let result: any = {};
        result["id"] = value._id;
        if (value._name) {
            result["name"] = value._name;
        }
        if (value._classification) {
            result["classification"] = value._classification;
        }
        if (value._releasePipelines) {
            result["releasePipelines"] = value._releasePipelines.map((e: any) => ReleasePipeline.toRawObject(e));
        }
        return result;
    }
}

export class DefaultChannel {
    public constructor(
        {
            id,
            repository,
            branch,
            channel,
        }: {
            id: number,
            repository: string,
            branch?: string,
            channel?: Channel,
        }
    ) {
        this._id = id;
        this._repository = repository;
        this._branch = branch;
        this._channel = channel;
    }

    private _id: number;

    public get id(): number {
        return this._id;
    }

    public set id(__value: number) {
        this._id = __value;
    }

    private _repository: string;

    public get repository(): string {
        return this._repository;
    }

    public set repository(__value: string) {
        this._repository = __value;
    }

    private _branch?: string;

    public get branch(): string | undefined {
        return this._branch;
    }

    public set branch(__value: string | undefined) {
        this._branch = __value;
    }

    private _channel?: Channel;

    public get channel(): Channel | undefined {
        return this._channel;
    }

    public set channel(__value: Channel | undefined) {
        this._channel = __value;
    }
    
    public isValid(): boolean {
        return (
            this._id !== undefined &&
            this._repository !== undefined
        );
    }

    public static fromRawObject(value: any): DefaultChannel {
        let result = new DefaultChannel({
            id: value["id"] == null ? undefined : value["id"] as any,
            repository: value["repository"] == null ? undefined : value["repository"] as any,
            branch: value["branch"] == null ? undefined : value["branch"] as any,
            channel: value["channel"] == null ? undefined : Channel.fromRawObject(value["channel"]) as any,
        });
        return result;
    }

    public static toRawObject(value: DefaultChannel): any {
        let result: any = {};
        result["id"] = value._id;
        result["repository"] = value._repository;
        if (value._branch) {
            result["branch"] = value._branch;
        }
        if (value._channel) {
            result["channel"] = Channel.toRawObject(value._channel);
        }
        return result;
    }
}

export class MergePolicy {
    public constructor(
        {
            name,
            properties,
        }: {
            name?: string,
            properties?: Record<string, any>,
        }
    ) {
        this._name = name;
        this._properties = properties;
    }

    private _name?: string;

    public get name(): string | undefined {
        return this._name;
    }

    public set name(__value: string | undefined) {
        this._name = __value;
    }

    private _properties?: Record<string, any>;

    public get properties(): Record<string, any> | undefined {
        return this._properties;
    }

    public set properties(__value: Record<string, any> | undefined) {
        this._properties = __value;
    }

    public static fromRawObject(value: any): MergePolicy {
        let result = new MergePolicy({
            name: value["name"] == null ? undefined : value["name"] as any,
            properties: value["properties"] == null ? undefined : Helper.mapValues(value["properties"], (v: any) => v) as any,
        });
        return result;
    }

    public static toRawObject(value: MergePolicy): any {
        let result: any = {};
        if (value._name) {
            result["name"] = value._name;
        }
        if (value._properties) {
            result["properties"] = Helper.mapValues(value._properties, (v: any) => v);
        }
        return result;
    }
}

export class PostData {
    public constructor(
        {
            repository,
            branch,
            channelId,
        }: {
            repository: string,
            branch: string,
            channelId: number,
        }
    ) {
        this._repository = repository;
        this._branch = branch;
        this._channelId = channelId;
    }

    private _repository: string;

    public get repository(): string {
        return this._repository;
    }

    public set repository(__value: string) {
        this._repository = __value;
    }

    private _branch: string;

    public get branch(): string {
        return this._branch;
    }

    public set branch(__value: string) {
        this._branch = __value;
    }

    private _channelId: number;

    public get channelId(): number {
        return this._channelId;
    }

    public set channelId(__value: number) {
        this._channelId = __value;
    }
    
    public isValid(): boolean {
        return (
            this._repository !== undefined &&
            this._branch !== undefined &&
            this._channelId !== undefined
        );
    }

    public static fromRawObject(value: any): PostData {
        let result = new PostData({
            repository: value["repository"] == null ? undefined : value["repository"] as any,
            branch: value["branch"] == null ? undefined : value["branch"] as any,
            channelId: value["channelId"] == null ? undefined : value["channelId"] as any,
        });
        return result;
    }

    public static toRawObject(value: PostData): any {
        let result: any = {};
        result["repository"] = value._repository;
        result["branch"] = value._branch;
        result["channelId"] = value._channelId;
        return result;
    }
}

export class ReleasePipeline {
    public constructor(
        {
            id,
            pipelineIdentifier,
            organization,
            project,
        }: {
            id: number,
            pipelineIdentifier: number,
            organization?: string,
            project?: string,
        }
    ) {
        this._id = id;
        this._pipelineIdentifier = pipelineIdentifier;
        this._organization = organization;
        this._project = project;
    }

    private _id: number;

    public get id(): number {
        return this._id;
    }

    public set id(__value: number) {
        this._id = __value;
    }

    private _pipelineIdentifier: number;

    public get pipelineIdentifier(): number {
        return this._pipelineIdentifier;
    }

    public set pipelineIdentifier(__value: number) {
        this._pipelineIdentifier = __value;
    }

    private _organization?: string;

    public get organization(): string | undefined {
        return this._organization;
    }

    public set organization(__value: string | undefined) {
        this._organization = __value;
    }

    private _project?: string;

    public get project(): string | undefined {
        return this._project;
    }

    public set project(__value: string | undefined) {
        this._project = __value;
    }
    
    public isValid(): boolean {
        return (
            this._id !== undefined &&
            this._pipelineIdentifier !== undefined
        );
    }

    public static fromRawObject(value: any): ReleasePipeline {
        let result = new ReleasePipeline({
            id: value["id"] == null ? undefined : value["id"] as any,
            pipelineIdentifier: value["pipelineIdentifier"] == null ? undefined : value["pipelineIdentifier"] as any,
            organization: value["organization"] == null ? undefined : value["organization"] as any,
            project: value["project"] == null ? undefined : value["project"] as any,
        });
        return result;
    }

    public static toRawObject(value: ReleasePipeline): any {
        let result: any = {};
        result["id"] = value._id;
        result["pipelineIdentifier"] = value._pipelineIdentifier;
        if (value._organization) {
            result["organization"] = value._organization;
        }
        if (value._project) {
            result["project"] = value._project;
        }
        return result;
    }
}

export class RepositoryHistoryItem {
    public constructor(
        {
            repositoryName,
            branchName,
            timestamp,
            errorMessage,
            success,
            action,
            retryUrl,
        }: {
            repositoryName?: string,
            branchName?: string,
            timestamp: Date,
            errorMessage?: string,
            success: boolean,
            action?: string,
            retryUrl?: string,
        }
    ) {
        this._repositoryName = repositoryName;
        this._branchName = branchName;
        this._timestamp = timestamp;
        this._errorMessage = errorMessage;
        this._success = success;
        this._action = action;
        this._retryUrl = retryUrl;
    }

    private _repositoryName?: string;

    public get repositoryName(): string | undefined {
        return this._repositoryName;
    }

    private _branchName?: string;

    public get branchName(): string | undefined {
        return this._branchName;
    }

    private _timestamp: Date;

    public get timestamp(): Date {
        return this._timestamp;
    }

    private _errorMessage?: string;

    public get errorMessage(): string | undefined {
        return this._errorMessage;
    }

    private _success: boolean;

    public get success(): boolean {
        return this._success;
    }

    private _action?: string;

    public get action(): string | undefined {
        return this._action;
    }

    private _retryUrl?: string;

    public get retryUrl(): string | undefined {
        return this._retryUrl;
    }
    
    public isValid(): boolean {
        return (
            this._timestamp !== undefined &&
            this._success !== undefined
        );
    }

    public static fromRawObject(value: any): RepositoryHistoryItem {
        let result = new RepositoryHistoryItem({
            repositoryName: value["repositoryName"] == null ? undefined : value["repositoryName"] as any,
            branchName: value["branchName"] == null ? undefined : value["branchName"] as any,
            timestamp: value["timestamp"] == null ? undefined : parseISO(value["timestamp"]) as any,
            errorMessage: value["errorMessage"] == null ? undefined : value["errorMessage"] as any,
            success: value["success"] == null ? undefined : value["success"] as any,
            action: value["action"] == null ? undefined : value["action"] as any,
            retryUrl: value["retryUrl"] == null ? undefined : value["retryUrl"] as any,
        });
        return result;
    }

    public static toRawObject(value: RepositoryHistoryItem): any {
        let result: any = {};
        if (value._repositoryName) {
            result["repositoryName"] = value._repositoryName;
        }
        if (value._branchName) {
            result["branchName"] = value._branchName;
        }
        result["timestamp"] = value._timestamp.toISOString();
        if (value._errorMessage) {
            result["errorMessage"] = value._errorMessage;
        }
        result["success"] = value._success;
        if (value._action) {
            result["action"] = value._action;
        }
        if (value._retryUrl) {
            result["retryUrl"] = value._retryUrl;
        }
        return result;
    }
}

export class Subscription {
    public constructor(
        {
            id,
            channel,
            sourceRepository,
            targetRepository,
            targetBranch,
            policy,
            lastAppliedBuild,
            enabled,
        }: {
            id: string,
            channel?: Channel,
            sourceRepository?: string,
            targetRepository?: string,
            targetBranch?: string,
            policy?: SubscriptionPolicy,
            lastAppliedBuild?: Build,
            enabled: boolean,
        }
    ) {
        this._id = id;
        this._channel = channel;
        this._sourceRepository = sourceRepository;
        this._targetRepository = targetRepository;
        this._targetBranch = targetBranch;
        this._policy = policy;
        this._lastAppliedBuild = lastAppliedBuild;
        this._enabled = enabled;
    }

    private _id: string;

    public get id(): string {
        return this._id;
    }

    private _channel?: Channel;

    public get channel(): Channel | undefined {
        return this._channel;
    }

    public set channel(__value: Channel | undefined) {
        this._channel = __value;
    }

    private _sourceRepository?: string;

    public get sourceRepository(): string | undefined {
        return this._sourceRepository;
    }

    private _targetRepository?: string;

    public get targetRepository(): string | undefined {
        return this._targetRepository;
    }

    private _targetBranch?: string;

    public get targetBranch(): string | undefined {
        return this._targetBranch;
    }

    private _policy?: SubscriptionPolicy;

    public get policy(): SubscriptionPolicy | undefined {
        return this._policy;
    }

    public set policy(__value: SubscriptionPolicy | undefined) {
        this._policy = __value;
    }

    private _lastAppliedBuild?: Build;

    public get lastAppliedBuild(): Build | undefined {
        return this._lastAppliedBuild;
    }

    public set lastAppliedBuild(__value: Build | undefined) {
        this._lastAppliedBuild = __value;
    }

    private _enabled: boolean;

    public get enabled(): boolean {
        return this._enabled;
    }
    
    public isValid(): boolean {
        return (
            this._id !== undefined &&
            this._enabled !== undefined
        );
    }

    public static fromRawObject(value: any): Subscription {
        let result = new Subscription({
            id: value["id"] == null ? undefined : value["id"] as any,
            channel: value["channel"] == null ? undefined : Channel.fromRawObject(value["channel"]) as any,
            sourceRepository: value["sourceRepository"] == null ? undefined : value["sourceRepository"] as any,
            targetRepository: value["targetRepository"] == null ? undefined : value["targetRepository"] as any,
            targetBranch: value["targetBranch"] == null ? undefined : value["targetBranch"] as any,
            policy: value["policy"] == null ? undefined : SubscriptionPolicy.fromRawObject(value["policy"]) as any,
            lastAppliedBuild: value["lastAppliedBuild"] == null ? undefined : Build.fromRawObject(value["lastAppliedBuild"]) as any,
            enabled: value["enabled"] == null ? undefined : value["enabled"] as any,
        });
        return result;
    }

    public static toRawObject(value: Subscription): any {
        let result: any = {};
        result["id"] = value._id;
        if (value._channel) {
            result["channel"] = Channel.toRawObject(value._channel);
        }
        if (value._sourceRepository) {
            result["sourceRepository"] = value._sourceRepository;
        }
        if (value._targetRepository) {
            result["targetRepository"] = value._targetRepository;
        }
        if (value._targetBranch) {
            result["targetBranch"] = value._targetBranch;
        }
        if (value._policy) {
            result["policy"] = SubscriptionPolicy.toRawObject(value._policy);
        }
        if (value._lastAppliedBuild) {
            result["lastAppliedBuild"] = Build.toRawObject(value._lastAppliedBuild);
        }
        result["enabled"] = value._enabled;
        return result;
    }
}

export class SubscriptionData {
    public constructor(
        {
            channelName,
            sourceRepository,
            targetRepository,
            targetBranch,
            enabled,
            policy,
        }: {
            channelName: string,
            sourceRepository: string,
            targetRepository: string,
            targetBranch: string,
            enabled?: boolean,
            policy: SubscriptionPolicy,
        }
    ) {
        this._channelName = channelName;
        this._sourceRepository = sourceRepository;
        this._targetRepository = targetRepository;
        this._targetBranch = targetBranch;
        this._enabled = enabled;
        this._policy = policy;
    }

    private _channelName: string;

    public get channelName(): string {
        return this._channelName;
    }

    public set channelName(__value: string) {
        this._channelName = __value;
    }

    private _sourceRepository: string;

    public get sourceRepository(): string {
        return this._sourceRepository;
    }

    public set sourceRepository(__value: string) {
        this._sourceRepository = __value;
    }

    private _targetRepository: string;

    public get targetRepository(): string {
        return this._targetRepository;
    }

    public set targetRepository(__value: string) {
        this._targetRepository = __value;
    }

    private _targetBranch: string;

    public get targetBranch(): string {
        return this._targetBranch;
    }

    public set targetBranch(__value: string) {
        this._targetBranch = __value;
    }

    private _enabled?: boolean;

    public get enabled(): boolean | undefined {
        return this._enabled;
    }

    public set enabled(__value: boolean | undefined) {
        this._enabled = __value;
    }

    private _policy: SubscriptionPolicy;

    public get policy(): SubscriptionPolicy {
        return this._policy;
    }

    public set policy(__value: SubscriptionPolicy) {
        this._policy = __value;
    }
    
    public isValid(): boolean {
        return (
            this._channelName !== undefined &&
            this._sourceRepository !== undefined &&
            this._targetRepository !== undefined &&
            this._targetBranch !== undefined &&
            this._policy !== undefined
        );
    }

    public static fromRawObject(value: any): SubscriptionData {
        let result = new SubscriptionData({
            channelName: value["channelName"] == null ? undefined : value["channelName"] as any,
            sourceRepository: value["sourceRepository"] == null ? undefined : value["sourceRepository"] as any,
            targetRepository: value["targetRepository"] == null ? undefined : value["targetRepository"] as any,
            targetBranch: value["targetBranch"] == null ? undefined : value["targetBranch"] as any,
            enabled: value["enabled"] == null ? undefined : value["enabled"] as any,
            policy: value["policy"] == null ? undefined : SubscriptionPolicy.fromRawObject(value["policy"]) as any,
        });
        return result;
    }

    public static toRawObject(value: SubscriptionData): any {
        let result: any = {};
        result["channelName"] = value._channelName;
        result["sourceRepository"] = value._sourceRepository;
        result["targetRepository"] = value._targetRepository;
        result["targetBranch"] = value._targetBranch;
        if (value._enabled) {
            result["enabled"] = value._enabled;
        }
        result["policy"] = SubscriptionPolicy.toRawObject(value._policy);
        return result;
    }
}

export class SubscriptionHistoryItem {
    public constructor(
        {
            timestamp,
            errorMessage,
            success,
            subscriptionId,
            action,
            retryUrl,
        }: {
            timestamp: Date,
            errorMessage?: string,
            success: boolean,
            subscriptionId: string,
            action?: string,
            retryUrl?: string,
        }
    ) {
        this._timestamp = timestamp;
        this._errorMessage = errorMessage;
        this._success = success;
        this._subscriptionId = subscriptionId;
        this._action = action;
        this._retryUrl = retryUrl;
    }

    private _timestamp: Date;

    public get timestamp(): Date {
        return this._timestamp;
    }

    private _errorMessage?: string;

    public get errorMessage(): string | undefined {
        return this._errorMessage;
    }

    private _success: boolean;

    public get success(): boolean {
        return this._success;
    }

    private _subscriptionId: string;

    public get subscriptionId(): string {
        return this._subscriptionId;
    }

    private _action?: string;

    public get action(): string | undefined {
        return this._action;
    }

    private _retryUrl?: string;

    public get retryUrl(): string | undefined {
        return this._retryUrl;
    }
    
    public isValid(): boolean {
        return (
            this._timestamp !== undefined &&
            this._success !== undefined &&
            this._subscriptionId !== undefined
        );
    }

    public static fromRawObject(value: any): SubscriptionHistoryItem {
        let result = new SubscriptionHistoryItem({
            timestamp: value["timestamp"] == null ? undefined : parseISO(value["timestamp"]) as any,
            errorMessage: value["errorMessage"] == null ? undefined : value["errorMessage"] as any,
            success: value["success"] == null ? undefined : value["success"] as any,
            subscriptionId: value["subscriptionId"] == null ? undefined : value["subscriptionId"] as any,
            action: value["action"] == null ? undefined : value["action"] as any,
            retryUrl: value["retryUrl"] == null ? undefined : value["retryUrl"] as any,
        });
        return result;
    }

    public static toRawObject(value: SubscriptionHistoryItem): any {
        let result: any = {};
        result["timestamp"] = value._timestamp.toISOString();
        if (value._errorMessage) {
            result["errorMessage"] = value._errorMessage;
        }
        result["success"] = value._success;
        result["subscriptionId"] = value._subscriptionId;
        if (value._action) {
            result["action"] = value._action;
        }
        if (value._retryUrl) {
            result["retryUrl"] = value._retryUrl;
        }
        return result;
    }
}

export class SubscriptionPolicy {
    public constructor(
        {
            batchable,
            updateFrequency,
            mergePolicies,
        }: {
            batchable: boolean,
            updateFrequency: SubscriptionPolicyUpdateFrequency,
            mergePolicies?: MergePolicy[],
        }
    ) {
        this._batchable = batchable;
        this._updateFrequency = updateFrequency;
        this._mergePolicies = mergePolicies;
    }

    private _batchable: boolean;

    public get batchable(): boolean {
        return this._batchable;
    }

    public set batchable(__value: boolean) {
        this._batchable = __value;
    }

    private _updateFrequency: SubscriptionPolicyUpdateFrequency;

    public get updateFrequency(): SubscriptionPolicyUpdateFrequency {
        return this._updateFrequency;
    }

    public set updateFrequency(__value: SubscriptionPolicyUpdateFrequency) {
        this._updateFrequency = __value;
    }

    private _mergePolicies?: MergePolicy[];

    public get mergePolicies(): MergePolicy[] | undefined {
        return this._mergePolicies;
    }

    public set mergePolicies(__value: MergePolicy[] | undefined) {
        this._mergePolicies = __value;
    }
    
    public isValid(): boolean {
        return (
            this._batchable !== undefined &&
            this._updateFrequency !== undefined
        );
    }

    public static fromRawObject(value: any): SubscriptionPolicy {
        let result = new SubscriptionPolicy({
            batchable: value["batchable"] == null ? undefined : value["batchable"] as any,
            updateFrequency: value["updateFrequency"] == null ? undefined : value["updateFrequency"] as any,
            mergePolicies: value["mergePolicies"] == null ? undefined : value["mergePolicies"].map((e: any) => MergePolicy.fromRawObject(e)) as any,
        });
        return result;
    }

    public static toRawObject(value: SubscriptionPolicy): any {
        let result: any = {};
        result["batchable"] = value._batchable;
        result["updateFrequency"] = value._updateFrequency;
        if (value._mergePolicies) {
            result["mergePolicies"] = value._mergePolicies.map((e: any) => MergePolicy.toRawObject(e));
        }
        return result;
    }
}

export enum SubscriptionPolicyUpdateFrequency {
    None = "none",
    EveryDay = "everyDay",
    EveryBuild = "everyBuild",
}

export class SubscriptionUpdate {
    public constructor(
        {
            channelName,
            sourceRepository,
            policy,
            enabled,
        }: {
            channelName?: string,
            sourceRepository?: string,
            policy?: SubscriptionPolicy,
            enabled?: boolean,
        }
    ) {
        this._channelName = channelName;
        this._sourceRepository = sourceRepository;
        this._policy = policy;
        this._enabled = enabled;
    }

    private _channelName?: string;

    public get channelName(): string | undefined {
        return this._channelName;
    }

    public set channelName(__value: string | undefined) {
        this._channelName = __value;
    }

    private _sourceRepository?: string;

    public get sourceRepository(): string | undefined {
        return this._sourceRepository;
    }

    public set sourceRepository(__value: string | undefined) {
        this._sourceRepository = __value;
    }

    private _policy?: SubscriptionPolicy;

    public get policy(): SubscriptionPolicy | undefined {
        return this._policy;
    }

    public set policy(__value: SubscriptionPolicy | undefined) {
        this._policy = __value;
    }

    private _enabled?: boolean;

    public get enabled(): boolean | undefined {
        return this._enabled;
    }

    public set enabled(__value: boolean | undefined) {
        this._enabled = __value;
    }

    public static fromRawObject(value: any): SubscriptionUpdate {
        let result = new SubscriptionUpdate({
            channelName: value["channelName"] == null ? undefined : value["channelName"] as any,
            sourceRepository: value["sourceRepository"] == null ? undefined : value["sourceRepository"] as any,
            policy: value["policy"] == null ? undefined : SubscriptionPolicy.fromRawObject(value["policy"]) as any,
            enabled: value["enabled"] == null ? undefined : value["enabled"] as any,
        });
        return result;
    }

    public static toRawObject(value: SubscriptionUpdate): any {
        let result: any = {};
        if (value._channelName) {
            result["channelName"] = value._channelName;
        }
        if (value._sourceRepository) {
            result["sourceRepository"] = value._sourceRepository;
        }
        if (value._policy) {
            result["policy"] = SubscriptionPolicy.toRawObject(value._policy);
        }
        if (value._enabled) {
            result["enabled"] = value._enabled;
        }
        return result;
    }
}
