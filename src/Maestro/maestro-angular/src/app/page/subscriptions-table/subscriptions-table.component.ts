import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { Subscription } from 'src/maestro-client/models';
import { Observable, of } from 'rxjs';
import { HttpClient } from '@angular/common/http';
import { map, shareReplay } from 'rxjs/operators';
import { StatefulResult, statefulPipe, statefulSwitchMap } from 'src/stateful';

class VersionDetails {

  // Dependency Records are <Name, Uri>
  public allDependencies: Record<string, string> = {};
  public missingDependencies: Record<string, number> = {};
  public unusedSubscriptions: Record<string, string> = {};

  constructor(inputFile: XMLDocument) {
    const productElements = inputFile.getElementsByTagName("ProductDependencies");
    let childProductElements = productElements[0].getElementsByTagName('Dependency');

    const toolsetElements = inputFile.getElementsByTagName("ToolsetDependencies");
    let childToolsetElements = productElements[0].getElementsByTagName('Dependency');

    for (let el of Array.from(childProductElements)) {
      this.parseDependencyElement(el, this.allDependencies);
    }

    for (let el of Array.from(childToolsetElements)){
      this.parseDependencyElement(el, this.allDependencies);
    }
  }

  parseDependencyElement(element: Element, dependencyList: Record<string, string>) {
    let dependencyName: string;
    let dependencyUri: string;

    const attributesForElement = element.attributes;
    const nameAttribute = attributesForElement.getNamedItem("Name");

    if (nameAttribute) {
      dependencyName = nameAttribute.nodeValue || "UnknownAssetName";
    }
    else {
      dependencyName = "UnknownAssetName";
    }

    const uriAttribute = element.getElementsByTagName("Uri").item(0);

    if (uriAttribute) {
      dependencyUri = uriAttribute.textContent || "UnknownUri";
    }
    else {
      dependencyUri = "UnknownUri";
    }
    dependencyList[dependencyName] = dependencyUri;
  }

  getMissingSubscriptions(subscriptions: Subscription[]) {
    let missingSubs: Record<string, number> = {};
    const sourceRepos = subscriptions.map((sub) => sub.sourceRepository);

    for (let uri of Object.values(this.allDependencies)) {
      if (!sourceRepos.includes(uri)) {
        missingSubs[uri] = (missingSubs[uri] || 0) + 1;
      }
    }

    this.missingDependencies = missingSubs;
  }

  getUnnecessarySubs(subscriptions: Subscription[]) {
    let extraSubs: Record<string, string> = {};
    const dependenciesUsed = Object.values(this.allDependencies);

    for (let sub of subscriptions) {
      if (sub.sourceRepository) {
        if (!dependenciesUsed.includes(sub.sourceRepository)) {
          extraSubs[sub.sourceRepository] = sub.sourceRepository;
        }
      }
    }

    this.unusedSubscriptions = extraSubs;
  }
}

@Component({
  selector: 'mc-subscriptions-table',
  templateUrl: './subscriptions-table.component.html',
  styleUrls: ['./subscriptions-table.component.scss']
})
export class SubscriptionsTableComponent implements OnChanges {

  @Input() public rootId?: number;
  @Input() public subscriptionsList?: Record<string, Subscription[]>;
  @Input() public includeSubToolsets?: boolean;
  public currentTab?: string;
  public open = false;
  public versionDetailsList: Record<string, Observable<StatefulResult<VersionDetails>>> = {};

  get branches() {
    return Object.keys(this.subscriptionsList || {});
  }

  loadVersionData() {
    if (this.subscriptionsList) {
      for (const branch of this.branches) {
        const fileUrl = getDetailsFileUrl(this.subscriptionsList[branch][0].targetRepository || "", branch);

        const fileRequest = of(fileUrl).pipe(
          statefulSwitchMap(
            (uri) => {
              return this.http.get(uri, { responseType: 'text' });
            }
          ),
          statefulPipe(
            map(
              (file) => {
                const xmlData = new DOMParser().parseFromString(file, "text/xml");
                return new VersionDetails(xmlData);
              }),
            map(
              (versionDetails) => {
                if (this.subscriptionsList) {
                  versionDetails.getMissingSubscriptions(this.subscriptionsList[branch]);
                  versionDetails.getUnnecessarySubs(this.subscriptionsList[branch]);
                }
                return versionDetails;
              }
            ),
            shareReplay(1)
          ));

        this.versionDetailsList[branch] = fileRequest;
      };
    }
  }

  constructor(private http: HttpClient) { }

  ngOnChanges(changes: SimpleChanges) {
    this.currentTab = this.branches[0];
    if ("subscriptionsList" in changes) {
      this.loadVersionData();
    }
  }
}

function getDetailsFileUrl(sourceRepoStr: string, branchName: string) {
  if (sourceRepoStr.includes("github")) {
    const temp = sourceRepoStr.replace("https://github.com", "https://raw.githubusercontent.com")
    console.log(temp + "/" + branchName + "/eng/Version.Details.xml");
    return temp + "/" + branchName + "/eng/Version.Details.xml";
  }
  else {
    return sourceRepoStr + "?path=%2Feng%2FVersion.Details.xml&version=GBmaster";
  }
}
