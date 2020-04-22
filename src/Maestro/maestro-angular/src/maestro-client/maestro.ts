import { NgModule, Injectable, Inject, InjectionToken } from "@angular/core";
import { HttpClientModule, HttpClient, HttpHeaders, HttpParams } from "@angular/common/http";
import { Observable } from "rxjs";
import { map } from "rxjs/operators";
import { parseISO } from "date-fns";

import * as models from "./models";
import { Helper } from "./helper";

type RequestOptions = {
  body?: any;
  headers?: HttpHeaders;
  params?: HttpParams;
  responseType?: "json" | "arraybuffer" | "blob" | "text";
};

export interface ICredentials {
    processRequest(method: string, uri: string, opts: RequestOptions): [string, string, RequestOptions];
}

export class TokenCredentials {
    constructor(public value: string, public type: string = "Bearer") {
    }

    processRequest(method: string, uri: string, opts: RequestOptions): [string, string, RequestOptions] {
        if (!opts || !opts.headers) {
            throw new Error("request options not valid");
        }
        opts.headers = opts.headers.set("Authorization", this.type + " " + this.value);
        return [method, uri, opts];
    }
}

export let MaestroOptionsToken = new InjectionToken<MaestroOptions>('maestroOptions');
export interface MaestroOptions {
    baseUrl: string;
    defaultHeaders: {
        [key: string]: string;
    };
    credentials?: ICredentials;
}

@Injectable()
export class MaestroService {
    public options: MaestroOptions;
    constructor(private http: HttpClient, @Inject(MaestroOptionsToken) options: MaestroOptions) {
        this.options = Object.assign({}, MaestroModule.defaultOptions, options);
        this.assets = new AssetsApiService(this);
        this.builds = new BuildsApiService(this);
        this.buildTime = new BuildTimeApiService(this);
        this.channels = new ChannelsApiService(this);
        this.defaultChannels = new DefaultChannelsApiService(this);
        this.goal = new GoalApiService(this);
        this.repository = new RepositoryApiService(this);
        this.subscriptions = new SubscriptionsApiService(this);
    }
    public assets: IAssetsApi;
    public builds: IBuildsApi;
    public buildTime: IBuildTimeApi;
    public channels: IChannelsApi;
    public defaultChannels: IDefaultChannelsApi;
    public goal: IGoalApi;
    public repository: IRepositoryApi;
    public subscriptions: ISubscriptionsApi;

    public request(method: string, uri: string, opts: RequestOptions): Observable<any> {
        if (this.options.credentials) {
            [method, uri, opts] = this.options.credentials.processRequest(method, uri, opts);
        }
        return this.http.request(method, uri, opts);
    }
}

@NgModule({
    imports: [
        HttpClientModule,
    ],
    providers: [
        MaestroService,
        { provide: MaestroOptionsToken, useValue: MaestroModule.defaultOptions },
    ],
})
export class MaestroModule {
    public static defaultOptions: MaestroOptions = {
        baseUrl: "https://maestro-prod.westus2.cloudapp.azure.com/",
        defaultHeaders: {},
    };

    public static forRoot(options?: Partial<MaestroOptions>) {
        return {
            ngModule: MaestroModule,
            providers: [
                { provide: MaestroOptionsToken, useValue: options || {} },
            ],
        };
    }
}

export interface IAssetsApi {

    bulkAddLocationsAsync(
        parameters: {
            body: models.AssetAndLocation[],
        }
    ): Observable<void>;

    listAssetsAsync(
        parameters: {
            buildId?: number,
            loadLocations?: boolean,
            name?: string,
            nonShipping?: boolean,
            page?: number,
            perPage?: number,
            version?: string,
        }
    ): Observable<models.Asset[]>;

    getDarcVersionAsync(
    ): Observable<string>;

    getAssetAsync(
        parameters: {
            id: number,
        }
    ): Observable<models.Asset>;

    addAssetLocationToAssetAsync(
        parameters: {
            assetId: number,
            assetLocationType: models.AddAssetLocationToAssetAssetLocationType,
            location: string,
        }
    ): Observable<models.AssetLocation>;

    removeAssetLocationFromAssetAsync(
        parameters: {
            assetId: number,
            assetLocationId: number,
        }
    ): Observable<void>;
}

export class AssetsApiService implements IAssetsApi {
    private options: MaestroOptions;
    constructor(public client: MaestroService) {
        this.options = client.options;
    }

    public bulkAddLocationsAsync(
        {
            body,
        }: {
            body: models.AssetAndLocation[],
        }
    ): Observable<void> {
        if (body === undefined) {
            throw new Error("Required parameter body is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/assets/bulk-add-locations";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "text",
                body: JSON.stringify(body.map((e: any) => models.AssetAndLocation.toRawObject(e))),
            }
        );

    }

    public listAssetsAsync(
        {
            buildId,
            loadLocations,
            name,
            nonShipping,
            page,
            perPage,
            version,
        }: {
            buildId?: number,
            loadLocations?: boolean,
            name?: string,
            nonShipping?: boolean,
            page?: number,
            perPage?: number,
            version?: string,
        }
    ): Observable<models.Asset[]> {
        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/assets";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (name)
        {
            queryParameters = queryParameters.set("name", name);
        }

        if (version)
        {
            queryParameters = queryParameters.set("version", version);
        }

        if (buildId)
        {
            queryParameters = queryParameters.set("buildId", buildId + "");
        }

        if (nonShipping)
        {
            queryParameters = queryParameters.set("nonShipping", nonShipping + "");
        }

        if (loadLocations)
        {
            queryParameters = queryParameters.set("loadLocations", loadLocations + "");
        }

        if (page)
        {
            queryParameters = queryParameters.set("page", page + "");
        }

        if (perPage)
        {
            queryParameters = queryParameters.set("perPage", perPage + "");
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => raw.map((e: any) => models.Asset.fromRawObject(e)))
        );

    }

    public getDarcVersionAsync(
    ): Observable<string> {
        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/assets/darc-version";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => raw)
        );

    }

    public getAssetAsync(
        {
            id,
        }: {
            id: number,
        }
    ): Observable<models.Asset> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/assets/{id}";
        _path = _path.replace("{id}", id + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.Asset.fromRawObject(raw))
        );

    }

    public addAssetLocationToAssetAsync(
        {
            assetId,
            assetLocationType,
            location,
        }: {
            assetId: number,
            assetLocationType: models.AddAssetLocationToAssetAssetLocationType,
            location: string,
        }
    ): Observable<models.AssetLocation> {
        if (assetId === undefined) {
            throw new Error("Required parameter assetId is undefined.");
        }

        if (assetLocationType === undefined) {
            throw new Error("Required parameter assetLocationType is undefined.");
        }

        if (location === undefined) {
            throw new Error("Required parameter location is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/assets/{assetId}/locations";
        _path = _path.replace("{assetId}", assetId + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (location)
        {
            queryParameters = queryParameters.set("location", location);
        }

        if (assetLocationType)
        {
            queryParameters = queryParameters.set("assetLocationType", JSON.stringify(assetLocationType));
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.AssetLocation.fromRawObject(raw))
        );

    }

    public removeAssetLocationFromAssetAsync(
        {
            assetId,
            assetLocationId,
        }: {
            assetId: number,
            assetLocationId: number,
        }
    ): Observable<void> {
        if (assetId === undefined) {
            throw new Error("Required parameter assetId is undefined.");
        }

        if (assetLocationId === undefined) {
            throw new Error("Required parameter assetLocationId is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/assets/{assetId}/locations/{assetLocationId}";
        _path = _path.replace("{assetId}", assetId + "");
        _path = _path.replace("{assetLocationId}", assetLocationId + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "delete",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "text",
            }
        );

    }
}

export interface IBuildsApi {

    listBuildsAsync(
        parameters: {
            azdoAccount?: string,
            azdoBuildId?: number,
            azdoProject?: string,
            buildNumber?: string,
            channelId?: number,
            commit?: string,
            loadCollections?: boolean,
            notAfter?: Date,
            notBefore?: Date,
            page?: number,
            perPage?: number,
            repository?: string,
        }
    ): Observable<models.Build[]>;

    createAsync(
        parameters: {
            body: models.BuildData,
        }
    ): Observable<models.Build>;

    getBuildAsync(
        parameters: {
            id: number,
        }
    ): Observable<models.Build>;

    getBuildGraphAsync(
        parameters: {
            id: number,
        }
    ): Observable<models.BuildGraph>;

    getLatestAsync(
        parameters: {
            buildNumber?: string,
            channelId?: number,
            commit?: string,
            loadCollections?: boolean,
            notAfter?: Date,
            notBefore?: Date,
            repository?: string,
        }
    ): Observable<models.Build>;

    updateAsync(
        parameters: {
            body: models.BuildUpdate,
            buildId: number,
        }
    ): Observable<models.Build>;
}

export class BuildsApiService implements IBuildsApi {
    private options: MaestroOptions;
    constructor(public client: MaestroService) {
        this.options = client.options;
    }

    public listBuildsAsync(
        {
            azdoAccount,
            azdoBuildId,
            azdoProject,
            buildNumber,
            channelId,
            commit,
            loadCollections,
            notAfter,
            notBefore,
            page,
            perPage,
            repository,
        }: {
            azdoAccount?: string,
            azdoBuildId?: number,
            azdoProject?: string,
            buildNumber?: string,
            channelId?: number,
            commit?: string,
            loadCollections?: boolean,
            notAfter?: Date,
            notBefore?: Date,
            page?: number,
            perPage?: number,
            repository?: string,
        }
    ): Observable<models.Build[]> {
        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/builds";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (repository)
        {
            queryParameters = queryParameters.set("repository", repository);
        }

        if (commit)
        {
            queryParameters = queryParameters.set("commit", commit);
        }

        if (buildNumber)
        {
            queryParameters = queryParameters.set("buildNumber", buildNumber);
        }

        if (azdoBuildId)
        {
            queryParameters = queryParameters.set("azdoBuildId", azdoBuildId + "");
        }

        if (azdoAccount)
        {
            queryParameters = queryParameters.set("azdoAccount", azdoAccount);
        }

        if (azdoProject)
        {
            queryParameters = queryParameters.set("azdoProject", azdoProject);
        }

        if (channelId)
        {
            queryParameters = queryParameters.set("channelId", channelId + "");
        }

        if (notBefore)
        {
            queryParameters = queryParameters.set("notBefore", notBefore.toISOString());
        }

        if (notAfter)
        {
            queryParameters = queryParameters.set("notAfter", notAfter.toISOString());
        }

        if (loadCollections)
        {
            queryParameters = queryParameters.set("loadCollections", loadCollections + "");
        }

        if (page)
        {
            queryParameters = queryParameters.set("page", page + "");
        }

        if (perPage)
        {
            queryParameters = queryParameters.set("perPage", perPage + "");
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => raw.map((e: any) => models.Build.fromRawObject(e)))
        );

    }

    public createAsync(
        {
            body,
        }: {
            body: models.BuildData,
        }
    ): Observable<models.Build> {
        if (body === undefined) {
            throw new Error("Required parameter body is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/builds";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
                body: JSON.stringify(models.BuildData.toRawObject(body)),
            }
        ).pipe(
            map(raw => models.Build.fromRawObject(raw))
        );

    }

    public getBuildAsync(
        {
            id,
        }: {
            id: number,
        }
    ): Observable<models.Build> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/builds/{id}";
        _path = _path.replace("{id}", id + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.Build.fromRawObject(raw))
        );

    }

    public getBuildGraphAsync(
        {
            id,
        }: {
            id: number,
        }
    ): Observable<models.BuildGraph> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/builds/{id}/graph";
        _path = _path.replace("{id}", id + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.BuildGraph.fromRawObject(raw))
        );

    }

    public getLatestAsync(
        {
            buildNumber,
            channelId,
            commit,
            loadCollections,
            notAfter,
            notBefore,
            repository,
        }: {
            buildNumber?: string,
            channelId?: number,
            commit?: string,
            loadCollections?: boolean,
            notAfter?: Date,
            notBefore?: Date,
            repository?: string,
        }
    ): Observable<models.Build> {
        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/builds/latest";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (repository)
        {
            queryParameters = queryParameters.set("repository", repository);
        }

        if (commit)
        {
            queryParameters = queryParameters.set("commit", commit);
        }

        if (buildNumber)
        {
            queryParameters = queryParameters.set("buildNumber", buildNumber);
        }

        if (channelId)
        {
            queryParameters = queryParameters.set("channelId", channelId + "");
        }

        if (notBefore)
        {
            queryParameters = queryParameters.set("notBefore", notBefore.toISOString());
        }

        if (notAfter)
        {
            queryParameters = queryParameters.set("notAfter", notAfter.toISOString());
        }

        if (loadCollections)
        {
            queryParameters = queryParameters.set("loadCollections", loadCollections + "");
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.Build.fromRawObject(raw))
        );

    }

    public updateAsync(
        {
            body,
            buildId,
        }: {
            body: models.BuildUpdate,
            buildId: number,
        }
    ): Observable<models.Build> {
        if (body === undefined) {
            throw new Error("Required parameter body is undefined.");
        }

        if (buildId === undefined) {
            throw new Error("Required parameter buildId is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/builds/{buildId}";
        _path = _path.replace("{buildId}", buildId + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "patch",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
                body: JSON.stringify(models.BuildUpdate.toRawObject(body)),
            }
        ).pipe(
            map(raw => models.Build.fromRawObject(raw))
        );

    }
}

export interface IBuildTimeApi {

    getBuildTimesAsync(
        parameters: {
            days: number,
            id: number,
        }
    ): Observable<models.BuildTime>;
}

export class BuildTimeApiService implements IBuildTimeApi {
    private options: MaestroOptions;
    constructor(public client: MaestroService) {
        this.options = client.options;
    }

    public getBuildTimesAsync(
        {
            days,
            id,
        }: {
            days: number,
            id: number,
        }
    ): Observable<models.BuildTime> {
        if (days === undefined) {
            throw new Error("Required parameter days is undefined.");
        }

        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/buildtime/{id}";
        _path = _path.replace("{id}", id + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (days)
        {
            queryParameters = queryParameters.set("days", days + "");
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.BuildTime.fromRawObject(raw))
        );

    }
}

export interface IChannelsApi {

    listChannelsAsync(
        parameters: {
            classification?: string,
        }
    ): Observable<models.Channel[]>;

    createChannelAsync(
        parameters: {
            classification: string,
            name: string,
        }
    ): Observable<models.Channel>;

    listRepositoriesAsync(
        parameters: {
            id: number,
        }
    ): Observable<string[]>;

    getChannelAsync(
        parameters: {
            id: number,
        }
    ): Observable<models.Channel>;

    deleteChannelAsync(
        parameters: {
            id: number,
        }
    ): Observable<models.Channel>;

    addBuildToChannelAsync(
        parameters: {
            buildId: number,
            channelId: number,
        }
    ): Observable<void>;

    removeBuildFromChannelAsync(
        parameters: {
            buildId: number,
            channelId: number,
        }
    ): Observable<void>;

    getFlowGraphAsyncAsync(
        parameters: {
            channelId: number,
            days: number,
            includeArcade: boolean,
            includeBuildTimes: boolean,
            includeDisabledSubscriptions: boolean,
            includedFrequencies?: string[],
        }
    ): Observable<models.FlowGraph>;
}

export class ChannelsApiService implements IChannelsApi {
    private options: MaestroOptions;
    constructor(public client: MaestroService) {
        this.options = client.options;
    }

    public listChannelsAsync(
        {
            classification,
        }: {
            classification?: string,
        }
    ): Observable<models.Channel[]> {
        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/channels";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (classification)
        {
            queryParameters = queryParameters.set("classification", classification);
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => raw.map((e: any) => models.Channel.fromRawObject(e)))
        );

    }

    public createChannelAsync(
        {
            classification,
            name,
        }: {
            classification: string,
            name: string,
        }
    ): Observable<models.Channel> {
        if (classification === undefined) {
            throw new Error("Required parameter classification is undefined.");
        }

        if (name === undefined) {
            throw new Error("Required parameter name is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/channels";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (name)
        {
            queryParameters = queryParameters.set("name", name);
        }

        if (classification)
        {
            queryParameters = queryParameters.set("classification", classification);
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.Channel.fromRawObject(raw))
        );

    }

    public listRepositoriesAsync(
        {
            id,
        }: {
            id: number,
        }
    ): Observable<string[]> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/channels/{id}/repositories";
        _path = _path.replace("{id}", id + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => raw.map((e: any) => e))
        );

    }

    public getChannelAsync(
        {
            id,
        }: {
            id: number,
        }
    ): Observable<models.Channel> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/channels/{id}";
        _path = _path.replace("{id}", id + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.Channel.fromRawObject(raw))
        );

    }

    public deleteChannelAsync(
        {
            id,
        }: {
            id: number,
        }
    ): Observable<models.Channel> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/channels/{id}";
        _path = _path.replace("{id}", id + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "delete",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.Channel.fromRawObject(raw))
        );

    }

    public addBuildToChannelAsync(
        {
            buildId,
            channelId,
        }: {
            buildId: number,
            channelId: number,
        }
    ): Observable<void> {
        if (buildId === undefined) {
            throw new Error("Required parameter buildId is undefined.");
        }

        if (channelId === undefined) {
            throw new Error("Required parameter channelId is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/channels/{channelId}/builds/{buildId}";
        _path = _path.replace("{channelId}", channelId + "");
        _path = _path.replace("{buildId}", buildId + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "text",
            }
        );

    }

    public removeBuildFromChannelAsync(
        {
            buildId,
            channelId,
        }: {
            buildId: number,
            channelId: number,
        }
    ): Observable<void> {
        if (buildId === undefined) {
            throw new Error("Required parameter buildId is undefined.");
        }

        if (channelId === undefined) {
            throw new Error("Required parameter channelId is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/channels/{channelId}/builds/{buildId}";
        _path = _path.replace("{channelId}", channelId + "");
        _path = _path.replace("{buildId}", buildId + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "delete",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "text",
            }
        );

    }

    public getFlowGraphAsyncAsync(
        {
            channelId,
            days,
            includeArcade,
            includeBuildTimes,
            includeDisabledSubscriptions,
            includedFrequencies,
        }: {
            channelId: number,
            days: number,
            includeArcade: boolean,
            includeBuildTimes: boolean,
            includeDisabledSubscriptions: boolean,
            includedFrequencies?: string[],
        }
    ): Observable<models.FlowGraph> {
        if (channelId === undefined) {
            throw new Error("Required parameter channelId is undefined.");
        }

        if (days === undefined) {
            throw new Error("Required parameter days is undefined.");
        }

        if (includeArcade === undefined) {
            throw new Error("Required parameter includeArcade is undefined.");
        }

        if (includeBuildTimes === undefined) {
            throw new Error("Required parameter includeBuildTimes is undefined.");
        }

        if (includeDisabledSubscriptions === undefined) {
            throw new Error("Required parameter includeDisabledSubscriptions is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/channels/{channelId}/graph";
        _path = _path.replace("{channelId}", channelId + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (includeDisabledSubscriptions)
        {
            queryParameters = queryParameters.set("includeDisabledSubscriptions", includeDisabledSubscriptions + "");
        }

        if (includedFrequencies)
        {
            for (const _item of JSON.stringify(includedFrequencies.map((e: any) => e)))
            {
                queryParameters = queryParameters.append("includedFrequencies", _item);
            }
        }

        if (includeBuildTimes)
        {
            queryParameters = queryParameters.set("includeBuildTimes", includeBuildTimes + "");
        }

        if (days)
        {
            queryParameters = queryParameters.set("days", days + "");
        }

        if (includeArcade)
        {
            queryParameters = queryParameters.set("includeArcade", includeArcade + "");
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.FlowGraph.fromRawObject(raw))
        );

    }
}

export interface IDefaultChannelsApi {

    listAsync(
        parameters: {
            branch?: string,
            channelId?: number,
            enabled?: boolean,
            repository?: string,
        }
    ): Observable<models.DefaultChannel[]>;

    createAsync(
        parameters: {
            body: models.DefaultChannelCreateData,
        }
    ): Observable<void>;

    getAsync(
        parameters: {
            id: number,
        }
    ): Observable<models.DefaultChannel>;

    deleteAsync(
        parameters: {
            id: number,
        }
    ): Observable<void>;

    updateAsync(
        parameters: {
            id: number,
            body?: models.DefaultChannelUpdateData,
        }
    ): Observable<models.DefaultChannel>;
}

export class DefaultChannelsApiService implements IDefaultChannelsApi {
    private options: MaestroOptions;
    constructor(public client: MaestroService) {
        this.options = client.options;
    }

    public listAsync(
        {
            branch,
            channelId,
            enabled,
            repository,
        }: {
            branch?: string,
            channelId?: number,
            enabled?: boolean,
            repository?: string,
        }
    ): Observable<models.DefaultChannel[]> {
        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/default-channels";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (repository)
        {
            queryParameters = queryParameters.set("repository", repository);
        }

        if (branch)
        {
            queryParameters = queryParameters.set("branch", branch);
        }

        if (channelId)
        {
            queryParameters = queryParameters.set("channelId", channelId + "");
        }

        if (enabled)
        {
            queryParameters = queryParameters.set("enabled", enabled + "");
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => raw.map((e: any) => models.DefaultChannel.fromRawObject(e)))
        );

    }

    public createAsync(
        {
            body,
        }: {
            body: models.DefaultChannelCreateData,
        }
    ): Observable<void> {
        if (body === undefined) {
            throw new Error("Required parameter body is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/default-channels";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "text",
                body: JSON.stringify(models.DefaultChannelCreateData.toRawObject(body)),
            }
        );

    }

    public getAsync(
        {
            id,
        }: {
            id: number,
        }
    ): Observable<models.DefaultChannel> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/default-channels/{id}";
        _path = _path.replace("{id}", id + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.DefaultChannel.fromRawObject(raw))
        );

    }

    public deleteAsync(
        {
            id,
        }: {
            id: number,
        }
    ): Observable<void> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/default-channels/{id}";
        _path = _path.replace("{id}", id + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "delete",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "text",
            }
        );

    }

    public updateAsync(
        {
            id,
            body,
        }: {
            id: number,
            body?: models.DefaultChannelUpdateData,
        }
    ): Observable<models.DefaultChannel> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/default-channels/{id}";
        _path = _path.replace("{id}", id + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "patch",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
                body: body == undefined ? undefined : JSON.stringify(models.DefaultChannelUpdateData.toRawObject(body)),
            }
        ).pipe(
            map(raw => models.DefaultChannel.fromRawObject(raw))
        );

    }
}

export interface IGoalApi {

    getGoalTimesAsync(
        parameters: {
            channelName: string,
            definitionId: number,
        }
    ): Observable<models.Goal>;

    createAsync(
        parameters: {
            body: models.GoalRequestJson,
            channelName: string,
            definitionId: number,
        }
    ): Observable<models.Goal>;
}

export class GoalApiService implements IGoalApi {
    private options: MaestroOptions;
    constructor(public client: MaestroService) {
        this.options = client.options;
    }

    public getGoalTimesAsync(
        {
            channelName,
            definitionId,
        }: {
            channelName: string,
            definitionId: number,
        }
    ): Observable<models.Goal> {
        if (channelName === undefined) {
            throw new Error("Required parameter channelName is undefined.");
        }

        if (definitionId === undefined) {
            throw new Error("Required parameter definitionId is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/goals/channelName/{channelName}/definitionId/{definitionId}";
        _path = _path.replace("{definitionId}", definitionId + "");
        _path = _path.replace("{channelName}", channelName);

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.Goal.fromRawObject(raw))
        );

    }

    public createAsync(
        {
            body,
            channelName,
            definitionId,
        }: {
            body: models.GoalRequestJson,
            channelName: string,
            definitionId: number,
        }
    ): Observable<models.Goal> {
        if (body === undefined) {
            throw new Error("Required parameter body is undefined.");
        }

        if (channelName === undefined) {
            throw new Error("Required parameter channelName is undefined.");
        }

        if (definitionId === undefined) {
            throw new Error("Required parameter definitionId is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/goals/channelName/{channelName}/definitionId/{definitionId}";
        _path = _path.replace("{channelName}", channelName);
        _path = _path.replace("{definitionId}", definitionId + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "put",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
                body: JSON.stringify(models.GoalRequestJson.toRawObject(body)),
            }
        ).pipe(
            map(raw => models.Goal.fromRawObject(raw))
        );

    }
}

export interface IRepositoryApi {

    listRepositoriesAsync(
        parameters: {
            branch?: string,
            repository?: string,
        }
    ): Observable<models.RepositoryBranch[]>;

    getMergePoliciesAsync(
        parameters: {
            branch: string,
            repository: string,
        }
    ): Observable<models.MergePolicy[]>;

    setMergePoliciesAsync(
        parameters: {
            branch: string,
            repository: string,
            body?: models.MergePolicy[],
        }
    ): Observable<void>;

    getHistoryAsync(
        parameters: {
            branch?: string,
            page?: number,
            perPage?: number,
            repository?: string,
        }
    ): Observable<models.RepositoryHistoryItem[]>;

    retryActionAsyncAsync(
        parameters: {
            branch: string,
            repository: string,
            timestamp: number,
        }
    ): Observable<void>;
}

export class RepositoryApiService implements IRepositoryApi {
    private options: MaestroOptions;
    constructor(public client: MaestroService) {
        this.options = client.options;
    }

    public listRepositoriesAsync(
        {
            branch,
            repository,
        }: {
            branch?: string,
            repository?: string,
        }
    ): Observable<models.RepositoryBranch[]> {
        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/repo-config/repositories";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (repository)
        {
            queryParameters = queryParameters.set("repository", repository);
        }

        if (branch)
        {
            queryParameters = queryParameters.set("branch", branch);
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => raw.map((e: any) => models.RepositoryBranch.fromRawObject(e)))
        );

    }

    public getMergePoliciesAsync(
        {
            branch,
            repository,
        }: {
            branch: string,
            repository: string,
        }
    ): Observable<models.MergePolicy[]> {
        if (branch === undefined) {
            throw new Error("Required parameter branch is undefined.");
        }

        if (repository === undefined) {
            throw new Error("Required parameter repository is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/repo-config/merge-policy";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (repository)
        {
            queryParameters = queryParameters.set("repository", repository);
        }

        if (branch)
        {
            queryParameters = queryParameters.set("branch", branch);
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => raw.map((e: any) => models.MergePolicy.fromRawObject(e)))
        );

    }

    public setMergePoliciesAsync(
        {
            branch,
            repository,
            body,
        }: {
            branch: string,
            repository: string,
            body?: models.MergePolicy[],
        }
    ): Observable<void> {
        if (branch === undefined) {
            throw new Error("Required parameter branch is undefined.");
        }

        if (repository === undefined) {
            throw new Error("Required parameter repository is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/repo-config/merge-policy";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (repository)
        {
            queryParameters = queryParameters.set("repository", repository);
        }

        if (branch)
        {
            queryParameters = queryParameters.set("branch", branch);
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "text",
                body: body == undefined ? undefined : JSON.stringify(body.map((e: any) => models.MergePolicy.toRawObject(e))),
            }
        );

    }

    public getHistoryAsync(
        {
            branch,
            page,
            perPage,
            repository,
        }: {
            branch?: string,
            page?: number,
            perPage?: number,
            repository?: string,
        }
    ): Observable<models.RepositoryHistoryItem[]> {
        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/repo-config/history";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (repository)
        {
            queryParameters = queryParameters.set("repository", repository);
        }

        if (branch)
        {
            queryParameters = queryParameters.set("branch", branch);
        }

        if (page)
        {
            queryParameters = queryParameters.set("page", page + "");
        }

        if (perPage)
        {
            queryParameters = queryParameters.set("perPage", perPage + "");
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => raw.map((e: any) => models.RepositoryHistoryItem.fromRawObject(e)))
        );

    }

    public retryActionAsyncAsync(
        {
            branch,
            repository,
            timestamp,
        }: {
            branch: string,
            repository: string,
            timestamp: number,
        }
    ): Observable<void> {
        if (branch === undefined) {
            throw new Error("Required parameter branch is undefined.");
        }

        if (repository === undefined) {
            throw new Error("Required parameter repository is undefined.");
        }

        if (timestamp === undefined) {
            throw new Error("Required parameter timestamp is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/repo-config/retry/{timestamp}";
        _path = _path.replace("{timestamp}", timestamp + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (repository)
        {
            queryParameters = queryParameters.set("repository", repository);
        }

        if (branch)
        {
            queryParameters = queryParameters.set("branch", branch);
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "text",
            }
        );

    }
}

export interface ISubscriptionsApi {

    listSubscriptionsAsync(
        parameters: {
            channelId?: number,
            enabled?: boolean,
            sourceRepository?: string,
            targetRepository?: string,
        }
    ): Observable<models.Subscription[]>;

    createAsync(
        parameters: {
            body: models.SubscriptionData,
        }
    ): Observable<models.Subscription>;

    getSubscriptionAsync(
        parameters: {
            id: string,
        }
    ): Observable<models.Subscription>;

    deleteSubscriptionAsync(
        parameters: {
            id: string,
        }
    ): Observable<models.Subscription>;

    updateSubscriptionAsync(
        parameters: {
            id: string,
            body?: models.SubscriptionUpdate,
        }
    ): Observable<models.Subscription>;

    triggerSubscriptionAsync(
        parameters: {
            id: string,
        }
    ): Observable<models.Subscription>;

    triggerDailyUpdateAsync(
    ): Observable<void>;

    getSubscriptionHistoryAsync(
        parameters: {
            id: string,
            page?: number,
            perPage?: number,
        }
    ): Observable<models.SubscriptionHistoryItem[]>;

    retrySubscriptionActionAsyncAsync(
        parameters: {
            id: string,
            timestamp: number,
        }
    ): Observable<void>;
}

export class SubscriptionsApiService implements ISubscriptionsApi {
    private options: MaestroOptions;
    constructor(public client: MaestroService) {
        this.options = client.options;
    }

    public listSubscriptionsAsync(
        {
            channelId,
            enabled,
            sourceRepository,
            targetRepository,
        }: {
            channelId?: number,
            enabled?: boolean,
            sourceRepository?: string,
            targetRepository?: string,
        }
    ): Observable<models.Subscription[]> {
        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/subscriptions";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (sourceRepository)
        {
            queryParameters = queryParameters.set("sourceRepository", sourceRepository);
        }

        if (targetRepository)
        {
            queryParameters = queryParameters.set("targetRepository", targetRepository);
        }

        if (channelId)
        {
            queryParameters = queryParameters.set("channelId", channelId + "");
        }

        if (enabled)
        {
            queryParameters = queryParameters.set("enabled", enabled + "");
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => raw.map((e: any) => models.Subscription.fromRawObject(e)))
        );

    }

    public createAsync(
        {
            body,
        }: {
            body: models.SubscriptionData,
        }
    ): Observable<models.Subscription> {
        if (body === undefined) {
            throw new Error("Required parameter body is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/subscriptions";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
                body: JSON.stringify(models.SubscriptionData.toRawObject(body)),
            }
        ).pipe(
            map(raw => models.Subscription.fromRawObject(raw))
        );

    }

    public getSubscriptionAsync(
        {
            id,
        }: {
            id: string,
        }
    ): Observable<models.Subscription> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/subscriptions/{id}";
        _path = _path.replace("{id}", JSON.stringify(id));

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.Subscription.fromRawObject(raw))
        );

    }

    public deleteSubscriptionAsync(
        {
            id,
        }: {
            id: string,
        }
    ): Observable<models.Subscription> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/subscriptions/{id}";
        _path = _path.replace("{id}", JSON.stringify(id));

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "delete",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.Subscription.fromRawObject(raw))
        );

    }

    public updateSubscriptionAsync(
        {
            id,
            body,
        }: {
            id: string,
            body?: models.SubscriptionUpdate,
        }
    ): Observable<models.Subscription> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/subscriptions/{id}";
        _path = _path.replace("{id}", JSON.stringify(id));

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "patch",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
                body: body == undefined ? undefined : JSON.stringify(models.SubscriptionUpdate.toRawObject(body)),
            }
        ).pipe(
            map(raw => models.Subscription.fromRawObject(raw))
        );

    }

    public triggerSubscriptionAsync(
        {
            id,
        }: {
            id: string,
        }
    ): Observable<models.Subscription> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/subscriptions/{id}/trigger";
        _path = _path.replace("{id}", JSON.stringify(id));

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => models.Subscription.fromRawObject(raw))
        );

    }

    public triggerDailyUpdateAsync(
    ): Observable<void> {
        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/subscriptions/triggerDaily";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "text",
            }
        );

    }

    public getSubscriptionHistoryAsync(
        {
            id,
            page,
            perPage,
        }: {
            id: string,
            page?: number,
            perPage?: number,
        }
    ): Observable<models.SubscriptionHistoryItem[]> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/subscriptions/{id}/history";
        _path = _path.replace("{id}", JSON.stringify(id));

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (page)
        {
            queryParameters = queryParameters.set("page", page + "");
        }

        if (perPage)
        {
            queryParameters = queryParameters.set("perPage", perPage + "");
        }

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "get",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "json",
            }
        ).pipe(
            map(raw => raw.map((e: any) => models.SubscriptionHistoryItem.fromRawObject(e)))
        );

    }

    public retrySubscriptionActionAsyncAsync(
        {
            id,
            timestamp,
        }: {
            id: string,
            timestamp: number,
        }
    ): Observable<void> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        if (timestamp === undefined) {
            throw new Error("Required parameter timestamp is undefined.");
        }

        const apiVersion = "2020-02-20";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/subscriptions/{id}/retry/{timestamp}";
        _path = _path.replace("{id}", JSON.stringify(id));
        _path = _path.replace("{timestamp}", timestamp + "");

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        queryParameters = queryParameters.set("api-version", apiVersion);


        return this.client.request(
            "post",
            _path,
            {
                headers: headerParameters,
                params: queryParameters,
                responseType: "text",
            }
        );

    }
}
