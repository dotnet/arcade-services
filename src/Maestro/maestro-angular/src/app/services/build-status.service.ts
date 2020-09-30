import { Injectable } from "@angular/core";
import { Observable, of } from "rxjs";
import { HttpClient } from '@angular/common/http';
import { BuildStatus, CompletedBuild, BuildListResult, Build } from '../model/build-status';

@Injectable({
  providedIn: "root",
})
export class BuildStatusService {

  public constructor(private http: HttpClient) { }

  public getBranchStatus(account: string, project: string, definitionId: number, branch: string, count: number, status: "completed"): Observable<BuildListResult<CompletedBuild>>;
  public getBranchStatus(account: string, project: string, definitionId: number, branch: string, count: number, status: BuildStatus): Observable<BuildListResult<Build>>;

  public getBranchStatus(account: string, project: string, definitionId: number, branch: string, count: number, status: string): Observable<BuildListResult<Build> | BuildListResult<CompletedBuild>> {
    return this.http.get<BuildListResult<Build> | BuildListResult<CompletedBuild>>(`/_/AzDev/build/status/${account}/${project}/${definitionId}/${branch}?count=${count}&status=${status}`, {
      responseType: "json",
    })
  }
}
