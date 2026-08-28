import {HttpClient, HttpErrorResponse, HttpInterceptorFn} from '@angular/common/http';
import {inject, Injectable, signal} from '@angular/core';
import {catchError, forkJoin, Observable, tap, throwError} from 'rxjs';

export interface AuthUser{id:string;name:string;email:string;role:string}
export interface Summary{income:number;expenses:number;balance:number;saved:number;pendingDebt:number}
export interface Category{id:string;name:string;type:'income'|'expense';color:string}
export interface ApiTransaction{id:string;name:string;categoryId:string;category:string;date:string;amount:number;type:'income'|'expense';paymentMethod:string}
export interface Budget{id:string;categoryId:string|null;name:string;limit:number;used:number;month:number;year:number}
export interface Goal{id:string;name:string;targetAmount:number;currentAmount:number;targetDate:string;description:string}
export interface Debt{id:string;name:string;entity:string;totalAmount:number;paidAmount:number;dueDate:string;installments:number}
export interface FamilyUser{id:string;name:string;email:string;role:'Admin'|'Visitor'}

@Injectable({providedIn:'root'})
export class ApiService{
 private http=inject(HttpClient); private base='/api';
 authenticated=signal(!!localStorage.getItem('cashboard_token'));
 user=signal<AuthUser|null>(JSON.parse(localStorage.getItem('cashboard_user')||'null'));
 login(email:string,password:string){return this.http.post<{token:string;user:AuthUser}>(`${this.base}/auth/login`,{email,password}).pipe(tap(r=>{localStorage.setItem('cashboard_token',r.token);localStorage.setItem('cashboard_user',JSON.stringify(r.user));this.user.set(r.user);this.authenticated.set(true)}));}
 logout(){localStorage.removeItem('cashboard_token');localStorage.removeItem('cashboard_user');this.user.set(null);this.authenticated.set(false)}
 initialData(){return forkJoin({summary:this.http.get<Summary>(`${this.base}/dashboard/summary`),transactions:this.http.get<ApiTransaction[]>(`${this.base}/transactions`),categories:this.http.get<Category[]>(`${this.base}/categories`),budgets:this.http.get<Budget[]>(`${this.base}/budgets/current`),goals:this.http.get<Goal[]>(`${this.base}/savings-goals`),debts:this.http.get<Debt[]>(`${this.base}/debts`)})}
 createTransaction(body:{description:string;amount:number;type:string;categoryId:string;paymentMethod:string}){return this.http.post<ApiTransaction>(`${this.base}/transactions`,body)}
 deleteTransaction(id:string){return this.http.delete<void>(`${this.base}/transactions/${id}`)}
 save(resource:string,body:unknown,id?:string):Observable<void>{return id?this.http.put<void>(`${this.base}/${resource}/${id}`,body):this.http.post<void>(`${this.base}/${resource}`,body)}
 remove(resource:string,id:string){return this.http.delete<void>(`${this.base}/${resource}/${id}`)}
 getUsers(){return this.http.get<FamilyUser[]>(`${this.base}/users`)}
}

export const authInterceptor:HttpInterceptorFn=(request,next)=>{
 const token=localStorage.getItem('cashboard_token');
 return next(token?request.clone({setHeaders:{Authorization:`Bearer ${token}`}}):request).pipe(catchError((error:HttpErrorResponse)=>{if(error.status===401&&token){localStorage.removeItem('cashboard_token');localStorage.removeItem('cashboard_user');location.reload()}return throwError(()=>error)}));
};
