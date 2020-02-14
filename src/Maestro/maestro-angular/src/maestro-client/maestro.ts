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
        this.channels = new ChannelsApiService(this);
        this.defaultChannels = new DefaultChannelsApiService(this);
        this.pipelines = new PipelinesApiService(this);
        this.repository = new RepositoryApiService(this);
        this.subscriptions = new SubscriptionsApiService(this);
    }
    public assets: IAssetsApi;
    public builds: IBuildsApi;
    public channels: IChannelsApi;
    public defaultChannels: IDefaultChannelsApi;
    public pipelines: IPipelinesApi;
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
        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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
}

export class BuildsApiService implements IBuildsApi {
    private options: MaestroOptions;
    constructor(public client: MaestroService) {
        this.options = client.options;
    }

    public listBuildsAsync(
        {
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
        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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
        const apiVersion = "2019-01-16";
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
}

export interface IChannelsApi {

    listChannelsAsync(
        parameters: {
            classification?: string,
        }
    ): Observable<models.Channel[]>;

    listRepositoriesAsync(
        parameters: {
            id: number,
        }
    ): Observable<string[]>;

    createChannelAsync(
        parameters: {
            classification: string,
            name: string,
        }
    ): Observable<models.Channel>;

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

    addPipelineToChannelAsync(
        parameters: {
            channelId: number,
            pipelineId: number,
        }
    ): Observable<void>;

    deletePipelineFromChannelAsync(
        parameters: {
            channelId: number,
            pipelineId: number,
        }
    ): Observable<void>;
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
        const apiVersion = "2019-01-16";
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

    public listRepositoriesAsync(
        {
            id
        }: {
            id: number
        }
    ): Observable<string[]> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2019-01-16";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/")) {
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

    public addPipelineToChannelAsync(
        {
            channelId,
            pipelineId,
        }: {
            channelId: number,
            pipelineId: number,
        }
    ): Observable<void> {
        if (channelId === undefined) {
            throw new Error("Required parameter channelId is undefined.");
        }

        if (pipelineId === undefined) {
            throw new Error("Required parameter pipelineId is undefined.");
        }

        const apiVersion = "2019-01-16";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/channels/{channelId}/pipelines/{pipelineId}";
        _path = _path.replace("{channelId}", channelId + "");
        _path = _path.replace("{pipelineId}", pipelineId + "");

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

    public deletePipelineFromChannelAsync(
        {
            channelId,
            pipelineId,
        }: {
            channelId: number,
            pipelineId: number,
        }
    ): Observable<void> {
        if (channelId === undefined) {
            throw new Error("Required parameter channelId is undefined.");
        }

        if (pipelineId === undefined) {
            throw new Error("Required parameter pipelineId is undefined.");
        }

        const apiVersion = "2019-01-16";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/channels/{channelId}/pipelines/{pipelineId}";
        _path = _path.replace("{channelId}", channelId + "");
        _path = _path.replace("{pipelineId}", pipelineId + "");

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

export interface IDefaultChannelsApi {

    listAsync(
        parameters: {
            branch?: string,
            channelId?: number,
            repository?: string,
        }
    ): Observable<models.DefaultChannel[]>;

    createAsync(
        parameters: {
            body: models.PostData,
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
            repository,
        }: {
            branch?: string,
            channelId?: number,
            repository?: string,
        }
    ): Observable<models.DefaultChannel[]> {
        const apiVersion = "2019-01-16";
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
            body: models.PostData,
        }
    ): Observable<void> {
        if (body === undefined) {
            throw new Error("Required parameter body is undefined.");
        }

        const apiVersion = "2019-01-16";
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
                body: JSON.stringify(models.PostData.toRawObject(body)),
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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
}

export interface IPipelinesApi {

    listAsync(
        parameters: {
            organization?: string,
            pipelineIdentifier?: number,
            project?: string,
        }
    ): Observable<models.ReleasePipeline[]>;

    createPipelineAsync(
        parameters: {
            organization: string,
            pipelineIdentifier: number,
            project: string,
        }
    ): Observable<models.ReleasePipeline>;

    getPipelineAsync(
        parameters: {
            id: number,
        }
    ): Observable<models.ReleasePipeline>;

    deletePipelineAsync(
        parameters: {
            id: number,
        }
    ): Observable<models.ReleasePipeline>;
}

export class PipelinesApiService implements IPipelinesApi {
    private options: MaestroOptions;
    constructor(public client: MaestroService) {
        this.options = client.options;
    }

    public listAsync(
        {
            organization,
            pipelineIdentifier,
            project,
        }: {
            organization?: string,
            pipelineIdentifier?: number,
            project?: string,
        }
    ): Observable<models.ReleasePipeline[]> {
        const apiVersion = "2019-01-16";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/pipelines";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (pipelineIdentifier)
        {
            queryParameters = queryParameters.set("pipelineIdentifier", pipelineIdentifier + "");
        }

        if (organization)
        {
            queryParameters = queryParameters.set("organization", organization);
        }

        if (project)
        {
            queryParameters = queryParameters.set("project", project);
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
            map(raw => raw.map((e: any) => models.ReleasePipeline.fromRawObject(e)))
        );

    }

    public createPipelineAsync(
        {
            organization,
            pipelineIdentifier,
            project,
        }: {
            organization: string,
            pipelineIdentifier: number,
            project: string,
        }
    ): Observable<models.ReleasePipeline> {
        if (organization === undefined) {
            throw new Error("Required parameter organization is undefined.");
        }

        if (pipelineIdentifier === undefined) {
            throw new Error("Required parameter pipelineIdentifier is undefined.");
        }

        if (project === undefined) {
            throw new Error("Required parameter project is undefined.");
        }

        const apiVersion = "2019-01-16";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/pipelines";

        let queryParameters = new HttpParams();
        let headerParameters = new HttpHeaders(this.options.defaultHeaders);

        if (pipelineIdentifier)
        {
            queryParameters = queryParameters.set("pipelineIdentifier", pipelineIdentifier + "");
        }

        if (organization)
        {
            queryParameters = queryParameters.set("organization", organization);
        }

        if (project)
        {
            queryParameters = queryParameters.set("project", project);
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
            map(raw => models.ReleasePipeline.fromRawObject(raw))
        );

    }

    public getPipelineAsync(
        {
            id,
        }: {
            id: number,
        }
    ): Observable<models.ReleasePipeline> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2019-01-16";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/pipelines/{id}";
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
            map(raw => models.ReleasePipeline.fromRawObject(raw))
        );

    }

    public deletePipelineAsync(
        {
            id,
        }: {
            id: number,
        }
    ): Observable<models.ReleasePipeline> {
        if (id === undefined) {
            throw new Error("Required parameter id is undefined.");
        }

        const apiVersion = "2019-01-16";
        let _path = this.options.baseUrl;
        if (_path.endsWith("/"))
        {
            _path = _path.slice(0, -1);
        }
        _path = _path + "/api/pipelines/{id}";
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
            map(raw => models.ReleasePipeline.fromRawObject(raw))
        );

    }
}

export interface IRepositoryApi {

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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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
        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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
        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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
        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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

        const apiVersion = "2019-01-16";
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
