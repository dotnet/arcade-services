import { Injectable } from "@angular/core";
import { Observable, of } from "rxjs";
import { HttpClient } from '@angular/common/http';
import { BuildListResultInProgress, BuildListResultCompleted } from '../model/build-status';

@Injectable({
  providedIn: "root",
})
export class BuildStatusService {

  public constructor(private http: HttpClient) { }

  public getBranchStatus(account: string, project: string, definitionId: number, branch: string, count: number, status: "inProgress"): Observable<BuildListResultInProgress>;
  public getBranchStatus(account: string, project: string, definitionId: number, branch: string, count: number, status: "completed"): Observable<BuildListResultCompleted>;
  public getBranchStatus(account: string, project: string, definitionId: number, branch: string, count: number, status: string): Observable<BuildListResultInProgress | BuildListResultCompleted>;


  public getBranchStatus(account: string, project: string, definitionId: number, branch: string, count: number, status: string): Observable<BuildListResultInProgress | BuildListResultCompleted> {
    return this.http.get<BuildListResultInProgress>(`/_/AzDev/build/status/${account}/${project}/${definitionId}/${branch}?count=${count}&status=${status}`, {
      responseType: "json",
    })
  }
}
