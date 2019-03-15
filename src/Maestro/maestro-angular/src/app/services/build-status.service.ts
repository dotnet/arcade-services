import { Injectable } from "@angular/core";
import { Observable, of } from "rxjs";
import { HttpClient } from '@angular/common/http';
import { BuildListResult } from '../model/build-status';

@Injectable({
  providedIn: "root",
})
export class BuildStatusService {

  public constructor(private http: HttpClient) { }

  public getBranchStatus(account: string, project: string, definitionId: number, branch: string, count: number): Observable<BuildListResult> {
    return this.http.get<BuildListResult>(`/_/AzDev/build/status/${account}/${project}/${definitionId}/${branch}?count=${count}`, {
      responseType: "json",
    });
  }
}
